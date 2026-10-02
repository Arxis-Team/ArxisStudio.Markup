# Live documents

`ArxisStudio.Markup.Xaml.Loader` · `XamlLiveDocument`, `XamlLiveDocumentState`,
`XamlLiveDocumentChanges`, `XamlLiveEditResult`, `XamlExternalTextPolicy`, `XamlExternalTextResult`,
`XamlSessionReplacedEventArgs`

A tool that shows a form while it changes keeps three things in step on every change: the text, the
history that can take the change back, and the objects built from the text. A change can come from
the tool, from undo and redo, or from the file being written by another editor, and each of the three
can refuse it its own way. `XamlLiveDocument` is the one place they are kept together.

```csharp
await using var form = XamlLiveDocument.Open(uri, SourceText.From(await File.ReadAllTextAsync(path)));

form.SessionReplaced += (_, e) => canvas.Show(e.Current?.RootObject);   // on the UI thread
form.Changed += (_, e) => RefreshTitleAndCommands(form);

await form.AttachAsync(environment, new XamlLoadOptions { Mode = XamlLoadMode.Design });
```

ADR 0025 records why it is shaped this way.

## The text first

Every change is recorded in the text, then shown. An edit the objects cannot follow is still the
author's edit: it stays in the text and in the history, and what shows it says how far it got.

```csharp
XamlLiveEditResult result = await form.EditAsync(
    editor => editor.SetAttribute(
        selection.Resolve(editor.Document)!, XamlQualifiedName.Parse("Width"), "160"),
    "Resize Save");
```

The callback runs inside the document's turn, against the document as the operations before it left
it. Find elements in `editor.Document` — by `XamlElementPath`, by name — because an element read from
an earlier version belongs to another parse and the editor refuses it. The callback may run on another
thread than the caller's: record edits, touch nothing else. An editor that records nothing changes
nothing, and an exception leaves the document as it was.

`UndoAsync` and `RedoAsync` are steps the same way. `CanUndo`, `UndoDescription` and the redo pair
read the document's own history — opening is not a step of it.

## What shows the text

`State` says what the objects show, and `Diagnostics` why:

| State | Meaning |
| --- | --- |
| `Detached` | No environment is attached. The text and the history are there; nothing is built. |
| `Live` | The session shows the text as it reads now. |
| `Behind` | The session shows an earlier text. The current one does not parse, names a type nothing resolves, or holds a value nothing converts. |
| `Broken` | Nothing shows the document: it could not be loaded, and no earlier session can be believed. |

The route a change takes: the session updates in place where it can; where it cannot, a session is
built from the text and `SessionReplaced` is raised; where nothing can be built, the session in place
stays if its update was refused before anything was written, and the document is `Behind`. A text that
does not parse is never built from — a file saved halfway through a sentence is the ordinary case, and
the next save is usually the correction.

`XamlLiveEditResult` reports the operation: whether the text moved, whether the session was replaced,
the state it left, and the session's own `Update` and `Load` reports.

## The session changes hands

`SessionReplaced` is raised on the owning thread while the previous session is still open, so its root
and its map are there to take off a canvas. The previous session is disposed once every handler has
returned. The objects are yours: close a window the previous session built, and put the new root where
the old one was.

```csharp
form.SessionReplaced += (_, e) =>
{
    if (e.Previous?.RootObject is Window window)
    {
        card.Root = null;
        window.Close();
    }

    card.Root = e.Current?.RootObject;
};
```

A selection kept as a path survives this; a selection kept as an object does not.

## Saved, changed, and the file on disk

`IsDirty` is a difference of text against `SavedText`: undoing back to what was saved makes the
document clean again. After writing the file, say what was written:

```csharp
SourceText text = form.Document.SourceText;

await File.WriteAllTextAsync(path, text.ToString());
await form.MarkSavedAsync(text);
```

An edit that landed in between is not what was written, and the document goes on reading as changed —
which is why `MarkSavedAsync` takes the text rather than the document's word.

When the file changes on disk — the IDE beside the designer saving it — hand the text over with a
policy:

```csharp
XamlExternalTextResult result = await form.AcceptExternalTextAsync(
    disk, "Changed outside the designer", XamlExternalTextPolicy.ApplyIfClean);

if (result.Outcome == XamlExternalTextOutcome.Conflict)
{
    // Ask: take theirs (TakeTheirs) or keep mine (KeepMine).
}
```

| Outcome | What happened |
| --- | --- |
| `AlreadyCurrent` | The text is the document's own — the echo of its own save. It is now what is saved. |
| `Taken` | The text became the document's as one step of its history, and is what is saved. Undo takes it back. |
| `Conflict` | The document has unsaved changes and the policy said not to overwrite them. Nothing moved. |
| `KeptMine` | The document kept its text and now reads as changed against the file. |

`TakeTheirs` takes the text whatever is unsaved; the author's version is one undo away.

## Detached documents and generations

`DetachAsync` disposes the session and lets go of the environment and the options — the
`LocalAssembly`, a root access, resolvers over a generation of a project's assemblies — and keeps the
text, the history and what is saved. Do it to every document before replacing the assemblies its
environment was built over; nothing here holds the old generation afterwards. Edits, undo and text from
outside keep landing while a document is detached, and `AttachAsync` shows what the text then says.

A document nobody is looking at — a hidden tab — can stay detached until it is shown.

`RebuildAsync` builds the session again from the same text, for a change to something the text names
rather than to the text. `RetargetAsync` follows the file to a new path, keeping the history; the move
is not a step, and the session is built again because includes are found relative to the document.

A session handed to the next copy of a tool is restored with the text and what was saved:

```csharp
var restored = XamlLiveDocument.Open(uri, unsavedText, savedText: diskText);   // IsDirty, nothing to undo
```

## Order and threads

Operations run one at a time, in the order they were asked for, whatever thread asked. Events are
raised on the owning thread — the dispatcher given to `Open`, Avalonia's by default — once the
operation is over, so a handler sees the text, the session and the state together. Never block the
owning thread on an operation: it raises its events there.

A token gives up waiting for the turn. Once the text has moved, showing it is not cancelled.

## Writing through the document

Write through `EditAsync`, not through the session's `SetValue`. `SetValue` changes the session's
document behind the live document's back, and the next update compares against it — the rule
[Workspace and history](workspace.md) states for any workspace.
