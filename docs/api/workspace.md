# Workspace and history

Several open documents, versioned, with undo and redo over all of them at once.

## Why the library owns undo

Because the unit of undo is a change to a document's text, and because one thing a user does can
touch several files. Moving a control and changing the resource dictionary it reads from is one
action, and undoing half of it leaves the two disagreeing. Atomicity across documents is a property
of the set of open documents, so it belongs to whatever holds that set.

A tool built on these packages should not keep an undo stack of its own. See
[ADR 0007](../adr/0007-undo-belongs-to-the-workspace.md).

## Opening documents

`MarkupWorkspace` holds documents and their history and knows nothing about XAML. `XamlWorkspace`
is the seam: it parses, and it turns a structured edit into one entry in the history.

```csharp
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;

var workspace = new XamlWorkspace(new MarkupWorkspace(new FileMarkupSourceProvider()));

XamlDocument view = await workspace.OpenAsync(new Uri(viewPath), token);
XamlDocument theme = await workspace.OpenAsync(new Uri(themePath), token);
```

Any `IMarkupSourceProvider` will do — `FileMarkupSourceProvider` reads from disk,
`InMemoryMarkupSourceProvider` from a dictionary you control, `CompositeMarkupSourceProvider` tries
several in order. A host with unsaved buffers puts its own in front of the file one, and everything
downstream reads the unsaved text without knowing it.

Documents are identified by `MarkupDocumentId`:

```csharp
MarkupDocumentId id = workspace.Workspace.Documents.Single(open => open.Uri == view.Uri).Id;

XamlDocument current = workspace.GetDocument(id);   // the current parse; cached per document
```

## One edit, one undo entry

```csharp
XamlDocument edited = workspace.Apply(
    view.Edit().MoveElement(save, footer, 0),
    "Move Save into the footer");

workspace.CanUndo;          // true
workspace.UndoDescription;  // "Move Save into the footer"
workspace.Undo();           // the document is exactly what it was, character for character
workspace.Redo();
```

The description is what a user will be offered to undo, so name the action rather than the
mechanism: "Move Save into the footer", not "Apply 2 text changes".

Several documents, one action:

```csharp
ImmutableArray<XamlDocument> results = workspace.Apply(
    "Rename the accent brush",
    view.Edit().SetAttribute(title, foreground, "{StaticResource Highlight}"),
    theme.Edit().SetAttribute(brush, key, "Highlight"));
```

Either every editor lands or none does. Two editors for the same document are rejected: both were
computed against the same text, so applying both would apply neither correctly — record every edit
to one document in one editor.

An editor opened on a version the workspace has moved past is also rejected. Its changes are spans
into text that no longer exists, and approximating that is worse than saying so. Open a new editor
on `GetDocument(id)` after each application.

## Watching

```csharp
workspace.DocumentChanged += (_, e) =>
{
    // Raised for undo and redo as well as for an edit: to anything drawing the document those
    // are the same event.
    if (e.Kind == DocumentChangeKind.Changed && e.Document is { } document)
    {
        Redraw(document);
    }
};
```

`XamlWorkspace` is `IDisposable`, and disposing it unsubscribes from the workspace underneath. A
markup workspace outlives the views over it; one that was dropped without being disposed would go
on parsing every edit for as long as the workspace lived.

## Doing it by hand

Nothing stops you from driving `MarkupWorkspace` directly — `XamlWorkspace` is a hundred lines over
it, not a wall around it.

```csharp
using MarkupTransaction transaction = workspace.Workspace.BeginTransaction("Rename");

transaction.ApplyChanges(id, editor.GetTextChanges());
transaction.ApplyChanges(otherId, otherEditor.GetTextChanges());
transaction.Commit();       // or let the using block roll it back
```

A transaction that is disposed without `Commit` changes nothing; a failed one never leaves a
document partly written.

## Saving

There is no `Save`. Where a document goes is the host's business, and a library that wrote files
would be guessing about backups, encodings and permissions it knows nothing about.

```csharp
await File.WriteAllTextAsync(path, workspace.GetDocument(id).GetText(), token);
```

`GetText()` returns the document byte for byte, so saving an unchanged document is a copy rather
than a reformat.
