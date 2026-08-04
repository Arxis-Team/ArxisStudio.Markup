# Updates

Bringing running objects in line with a document that changed underneath them, without compiling
anything and without recreating what did not have to be recreated.

## Applying a changed document

```csharp
XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(edited, token);

result.Outcome;      // what it did to the objects: applied, refused, or stopped part-way
result.Strategy;     // the largest thing it would have taken
result.Changes;      // what was found, in document order
result.Diagnostics;
result.Applied;      // Outcome == Applied, for a caller that only wants to know whether to redraw
```

The comparison is over the syntax tree rather than the text, which is the point of having a
lossless one: reindenting a file, adding a comment or reflowing an attribute across two lines
changes every offset in it and changes nothing about the objects it describes. Those updates come
back as `Strategy.None` with no changes at all.

## What a failed update did

`Outcome` and `Strategy` answer two different questions, and a caller needs both. `Strategy` is
what the change *would have* taken; `Outcome` is what actually happened to the objects.

| `XamlUpdateOutcome` | The objects | The session |
| --- | --- | --- |
| `Applied` | moved to the new document | fine |
| `RejectedCleanly` | untouched | fine; try again with the next version |
| `RequiresNewSession` | part-way to the new document | **unusable**; build a new one |

`RejectedCleanly` is the ordinary failure and by far the common one. Everything that can be
checked is checked before the first live write: the document is compared, includes are resolved,
every fragment is built and every value converted. So a document caught halfway through being
typed, a value a member cannot hold, and a fragment that will not build all cost nothing at all.
The document that was offered is kept rather than dropped, because the next keystroke is usually
the correction:

```csharp
if (result.Outcome == XamlUpdateOutcome.RejectedCleanly && session.PendingDocument is { } refused)
{
    Show(result.Diagnostics, refused);   // and go on using the session
}
```

That covers values as well as syntax. `Margin="6,0,0,0"` is converted the way the same text is
converted at load — through the member's `TypeConverter`, or through the static `Parse` that Avalonia
types such as `Thickness` and `CornerRadius` are read by instead. Text the member cannot hold is an
ordinary user error: a diagnostic with the attribute's span, `RejectedCleanly`, the objects
untouched, and nothing thrown.

`RequiresNewSession` is what cannot be checked in advance: **user code**. The rule is simple and
deliberately blunt — *a refusal has to be reached without running the object's own code.* Once a
setter, an accessor or a collection method has been called and thrown, what it did first is
unknowable:

```csharp
set
{
    _value = value;                  // already happened
    Tag = Describe(value);           // so did this
    throw new InvalidOperationException("…and now I am unhappy.");
}
```

Nothing in the CLR or in Avalonia prevents that, and looking at the property afterwards would not
even notice the second line. So an exception out of a live setter is `RequiresNewSession` — *even
when it is the first change the update tried to make.* There is no generic rollback: what ran were
constructors, setters, converters and control code with side effects nothing here can reverse, so
instead of guessing, the session says so:

```csharp
if (result.Outcome == XamlUpdateOutcome.RequiresNewSession)
{
    // session.State is now XamlSessionState.RequiresNewSession, and every further
    // update or edit on it is refused with AXM3043.
    session = await XamlLoadSession.CreateAsync(session.PendingDocument!, environment, options, token);
}
```

`PendingDocument` is kept for exactly this, on **every** post-write failure including cancellation:
it is the state the caller was trying to reach and the one the objects are part-way towards. Once
set by the failure that broke the session it is not replaced — a later update that arrives and is
refused has no claim on the answer — and it is cleared only when a whole update is adopted.
Correct whatever the diagnostics report, then load it.

Reading a session in this state still works — a tool has to be able to show what it was holding —
but nothing that changes it does.

The blunt rule would be ruinous without two things in front of it. **Everything checkable is still
checked first**, and that is where clean refusals come from: the member exists, it can be written,
the text converts, the collection reports itself read-only. A typo in a property field never
reaches a setter. And Avalonia answers `IsReadOnly` on an items control's `Items` once
`ItemsSource` is bound, so the case a designer actually meets — a document adding a child to a
bound list — is refused before anything is invoked and stays clean.

A tool with a property field should ask before it writes — `XamlMemberDescriptor.ConvertFromText`
is the same conversion with no side effects, so half a value never reaches the undo history. See
[Loading](loading.md#is-this-text-a-value).

## One session mutates at a time

Every operation that changes a session — `ApplyDocumentUpdateAsync`, `ApplySourceUpdateAsync`,
`SetValue`, `SetXamlValue` — passes through one gate per session, because they all read and write
the same document, projection, object map and object tree. Two updates arriving together, which is
what a host watching a folder gets when a form and its dictionary are saved at once, cannot
interleave.

- **The asynchronous updates queue, first in first out.** The order is fixed when the call is made,
  not when a continuation happens to be scheduled, so three saves apply oldest first and the newest
  document is what the preview ends on. Each waiter observes its own cancellation token while it
  waits; giving up removes that one turn and strands nobody behind it.
- **The synchronous edits refuse.** `SetValue` and `SetXamlValue` cannot wait without blocking a
  thread that may be the one the update is dispatching to, so they return `Applied` false with
  `AXM3044` and write nothing. They also refuse when somebody is merely *waiting* — an edit
  unwilling to stand in the queue does not get to walk past it. Await the update and edit again.
- **Disposal waits** for an update already running rather than cutting it off. Queued work takes
  its turn, finds the session disposed and throws `ObjectDisposedException`; disposal does not
  deadlock behind it, and disposing twice at once is safe.
- **Whether the session is disposed, and whether it still describes its document, are decided
  inside the gate.** An answer read on the way in is an answer about a session somebody else may be
  in the middle of breaking.
- **Reading takes no lock at all** — the object map, `GetMembers`, `GetValueInfo`.

## Strategies

In increasing order of what each costs and how much it disturbs. An update takes the smallest one
that is certainly enough, and where a change could plausibly need either of two, it takes the
larger.

| Strategy | What happens |
| --- | --- |
| `None` | Nothing that affects an object changed |
| `SetProperty` | A literal on a writable member; the property is set where it stands |
| `UpdateDesignValue` | A design-time value; applied in design mode only |
| `ReorderChildren` | Named siblings changed places; the objects move, nothing is rebuilt |
| `ReplaceResource` | A dictionary entry is replaced |
| `ReloadStyle` | A style is rebuilt and put back where it was |
| `ReloadTheme` | A control theme is rebuilt and put back |
| `ReloadTemplate` | A template is rebuilt and its content recreated |
| `ReloadSubtree` | The affected element's objects are built again |
| `RecreateSession` | The root element or `x:Class` changed; make a new session |

`RecreateSession` is refused rather than attempted: the caller holds the root object and a session
is built around it, so there is nowhere to put a new one. It is refused *cleanly* — nothing is
written, and the session goes on describing the document it loaded until you replace it. This is
the case where the two properties differ most: `Outcome` says the session is fine, `Strategy` says
the new document is out of its reach.

```csharp
if (result.Strategy == XamlUpdateStrategy.RecreateSession)
{
    await session.DisposeAsync();
    session = await XamlLoadSession.CreateAsync(edited, environment, options, token);
}
```

Objects survive wherever they can. A `SetProperty` update leaves every object in place — a caller
holding one, or a selection pointing at one, is still valid afterwards — and a reorder moves the
objects that already exist rather than building new ones, so a control keeps its focus, its scroll
offset and whatever it was animating.

Where an element's objects live is read from the member the type marks `[Content]` — see
[Loading](loading.md#where-do-unnamed-children-go). A control library's own content member is
therefore replaced and reordered exactly as `Panel.Children` is, with nothing to register and no
base class to derive from.

## What a changed file costs

A document that includes other files is built from all of them, so a change to one of them is a
change to the load even though the document itself reads the same:

```csharp
inMemoryResources.Update(themeUri, newThemeText);

XamlUpdateResult result = await session.ApplySourceUpdateAsync(themeUri, token);
```

The document is reprojected — which is what re-reads the file through your resolvers — and the
difference that makes decides what is rebuilt. Being told a file changed is not evidence that
anything the document reaches did; when nothing did, the result is `None`.

To know in advance what one file costs, build the graph:

```csharp
var graph = new XamlResourceGraph(sourceProvider);

XamlResourceGraphResult built = await graph.BuildAsync(viewUri, token);

IReadOnlyCollection<Uri> reached = graph.Documents;
IReadOnlyCollection<Uri> uses = graph.GetDependencies(viewUri);
IReadOnlyCollection<Uri> affected = graph.GetDependents(themeUri);   // what to reload

await graph.UpdateAsync(themeUri, token);    // re-read one file, keep the rest
```

Cycles are detected and reported rather than followed.

## Design mode

`XamlLoadMode.Design` applies the document's design-time attributes; `Runtime` keeps them in the
document and does not apply them. The same text, loaded two ways.

```csharp
await using XamlLoadSession design = await XamlLoadSession.CreateAsync(
    document, environment, new XamlLoadOptions { Mode = XamlLoadMode.Design }, token);
```

Avalonia's own loader understands four names in the design namespace — `d:DesignWidth`,
`d:DesignHeight`, `d:DataContext`, `d:PreviewWith` — and fails the whole document on any other. So
every other design attribute is taken out of the text Avalonia is given and applied afterwards,
which is visible if you look:

```csharp
session.Document.GetText();          // still has every d: attribute
session.Projection.Text.ToString();  // what Avalonia was actually handed
```

`Projection` is the document with its includes spliced in and its design attributes removed, plus a
map back to the original offsets. It is how an object built from an included file is attributed to
that file rather than to whichever line of this one sits at the same number.

A design value is applied as a local value, so a binding on the same property overwrites it as soon
as a data context arrives — which is what a design value is for, since its purpose is to show a
document that has no data context yet.

## A tool's update loop

The whole thing, as a designer uses it:

```csharp
// 1. Record the edit and put it in the history.
XamlDocument edited = workspace.Apply(
    workspace.GetDocument(id).Edit().RemoveElement(subject),
    $"Delete <{subject.Name}>");

// 2. Bring the objects in line.
XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(edited, token);

// 3. Keep the two in agreement whatever happened. Which of the three happened decides how.
switch (result.Outcome)
{
    case XamlUpdateOutcome.Applied:
        await File.WriteAllTextAsync(path, session.Document.GetText(), token);
        break;

    case XamlUpdateOutcome.RejectedCleanly:
        workspace.Undo();      // the document goes back to what the objects still show
        Show(result.Diagnostics);
        break;

    case XamlUpdateOutcome.RequiresNewSession:
        // Undoing would be a lie: the objects moved and cannot be moved back. Reload instead,
        // and keep the edit — the user meant it, and the new session is built from it.
        Show(result.Diagnostics);
        await session.DisposeAsync();
        session = await XamlLoadSession.CreateAsync(edited, environment, options, token);
        break;
}
```

The third branch is the one worth writing before you need it. It is rare — it takes a control that
refuses a value its own type accepts — but a tool that treats every `Applied == false` as
"undo and carry on" will, on that day, undo a document the objects no longer match and go on
editing a tree that describes neither.

That last branch is the part worth copying. The document and the objects disagreeing is the one
state these packages exist to prevent, and a history holding an edit the tree never took would be
exactly that.

The showcase in `samples/ArxisStudio.Markup.Xaml.Loader.Sample` is this loop with a user interface
on it — a tree, a live preview, a property inspector, delete, duplicate, wrap, undo and redo, built
on the published API with nothing added to `src/`.
