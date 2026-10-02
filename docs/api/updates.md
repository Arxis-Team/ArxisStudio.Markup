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
`ApplyRebuildAsync`, `SetValue`, `SetXamlValue` — passes through one gate per session, because they all read and write
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
| `ClearProperty` | An attribute taken out, on an Avalonia property; its local value is cleared where it stands |
| `SetExpression` | A binding, a dynamic resource, a static member or a null; set where the property stands |
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

What an element holds is its content **and the members it writes as property elements**. Adding
`<Grid.Resources>`, a style to `<Window.Styles>` or a row to `<Grid.RowDefinitions>` changes the
element's children, and the rebuilt copy's member is moved onto the object that stays: a dictionary
is refilled — entries, merged dictionaries and theme dictionaries — and a list emptied and filled
again, so a `DynamicResource` inside picks the new entry up. A member written that way that holds a
single value — `<Border.Background>` — cannot be moved across, because what the copy reads back is a
value where the markup may have written a binding; the element's object is rebuilt and put back
instead. At the root there is nowhere to put it, so that one change is refused cleanly with
`Strategy` `RecreateSession`. A property element that reads the same in both documents is left
exactly as it is.

### Values set where they stand

A literal is converted and set. An attribute taken out of an Avalonia property clears the property's
local value — a style, a theme or an inherited value shows through again, which is what the document
now says — and that includes an attached property and the root's own attributes, so removing
`Background` from a window no longer costs a new session. A CLR property has no local value to clear:
only building the object again says what it holds when nobody wrote it, so that one rebuilds.

An expression is set in place when a load would turn it into a value or a binding with nothing to go
on but the element it is written on:

| Written | Set as |
| --- | --- |
| `{x:Null}` | null, where the member can hold one |
| `{x:Static prefix:Type.Member}` | the public static field's or property's value, where it fits the member |
| `{DynamicResource Key}` | a binding to the resource, which follows it when it changes |
| `{Binding …}` | a binding — `Path`, `Mode`, `StringFormat`, `ElementName`, `RelativeSource` (`Mode`, `AncestorType`, `AncestorLevel`), `FallbackValue`, `TargetNullValue` |

Everything else rebuilds the element: `{StaticResource}`, which is read once from the dictionaries in
scope while the element is built; a converter, which is one; `{CompiledBinding}`; a `{Binding}` where
bindings compile — under `x:CompileBindings="True"`, or with `UseCompiledBindingsByDefault` and nothing
in scope saying otherwise — because a compiled binding is checked against its data type when it is
built; and any argument not in the table. Whatever is written in place ends the binding the property
had first: a binding runs at local-value priority, and the next change of its source would otherwise
write over what the document now says.

`mc:Ignorable` that only gains namespaces nothing in the loaded document used is not a change at all:
a reader ignores markup in an ignorable namespace, and there was none. The first design value a tool
writes declares the design namespace and lists it — that used to cost a new session for a design
width. A namespace taken off the list, or one the loaded document has markup in, is structural as
before.

## What a rebuild keeps

A part rebuilt from the document is built on its own, through Avalonia's runtime loader, from a
projection of just that element. What that would lose, the session puts back:

- **The class is not constructed again.** The root's content is rebuilt from a copy of the root, and
  the copy is made without `x:Class` and the directives that go with it — the author's constructor
  runs once per session, and a window's copy is not a second window. The copy is closed once its
  content has moved across, and so is every part an update built and did not leave in the tree — an
  update that refuses, the root that asks for a new session among them; one that refuses to close is
  reported with `AXM3046`, because its platform window stays until the process ends.
- **Handlers are hooked up to the root.** A part is loaded with no instance whose methods its
  handlers could name, so they are left out of the part and hooked up to the session's root once the
  part's objects exist — never twice, and not on an element whose object stayed. A method of that name
  that cannot take the event's arguments is reported with `AXM3045`, a warning, and the rest of the
  update stands: a handler the author is still writing is the ordinary state of a file in an editor.
  A handler written, renamed or taken out is not a value to set — its element is built again and
  hooked up this way; on the root, whose handlers its load hooked up, it is a new session.
- **The part carries its scope.** `x:DataType` and `x:CompileBindings` of the nearest element above it
  are written onto the part's root, so a compiled binding inside it compiles as it did in the load.
- **A static reference reads the dictionaries around it.** A part whose markup reads a key with
  `{StaticResource}` that it does not declare itself is moved out to the element that declares it, and
  what that element holds is rebuilt instead; a key no element declares, where an element above
  includes another file, moves it out past the outermost such element. A key neither declared nor
  possibly included is the application's or a theme's, which a part built on its own still finds.
- **A rebuild inside another is the outer one's.** The outer rebuild builds the inner part as the
  document says, so the inner one is not built at all.
- **A control placed with markup of its own stays itself.** When the part's root is an `x:Class`
  control placed on the form, the session constructs it — its constructor populates it from its
  compiled markup, or from a [live document](live-population.md) registered for it — and loads the part
  onto it. Avalonia, left to construct it, took the part for the control's definition and populated the
  control from markup that only places it, and wrote `null` into the hook a live registration stands on.

All of it happens in one turn of the dispatcher: the writes, the map rebuilt over them, the design
values applied again and the handlers hooked up. Cancellation is observed before that turn and never
inside it, so an update cannot stop between writing the objects and adopting the document.

## A host that borrows the root

A tool that shows a `Window` cannot make it the content of anything, so it shows a stand-in that
borrows the window's content, resources and styles — and the session's next write would land on a
root whose content is somewhere else. A host that borrows says so through the options:

```csharp
sealed class BorrowedRoot(FormStandIn standIn) : IXamlRootAccess
{
    public IDisposable Lend(object root)
    {
        standIn.Return();               // the root holds its content, resources and styles again
        return new Reborrow(standIn);   // and the stand-in takes them back when the write is over
    }

    private sealed class Reborrow(FormStandIn standIn) : IDisposable
    {
        public void Dispose() => standIn.Borrow();
    }
}

var options = new XamlLoadOptions { Mode = XamlLoadMode.Design, RootAccess = new BorrowedRoot(standIn) };
```

`Lend` is called on the objects' thread around every write the session makes to the tree — an
update's whole turn, `SetValue`, the document side of `SetXamlValue` — and the lease is disposed when
the write is over, whether it landed or not. Nothing is lent for reading: the map and `GetValueInfo`
do not walk the root's children. A host that borrows nothing leaves `RootAccess` null.

## Rebuilding what the document did not change

A control written with `x:Class` is populated from its own markup when it is constructed, and an
instance already on a form goes on showing what that markup said then. When the control's own
document changes, the form's has not — and a host that has registered the new text for the class
asks for the elements that place it to be built again:

```csharp
await population.SetDocumentAsync(typeof(CustomerCard), editedCardDocument, token);

ImmutableArray<XamlElement> placed =
[
    .. session.Document.DescendantElements()
        .Where(element => session.GetObject(element) is CustomerCard),
];

XamlUpdateResult result = await session.ApplyRebuildAsync(placed, token);
```

Each element is rebuilt the way a change to it would be — with the smallest container it sits in, and
with whatever around it a static reference inside it reads — and comes back populated from the
registered document. The elements are of `session.Document` as it stands when the rebuild's turn
comes: one an earlier update has replaced since is refused with `AXM3041` and nothing written, so find
it again and ask again. The root is not rebuilt this way, because the session is built around it:
asking for it is refused with `RecreateSession`, and a new session from the same document is what
builds it again.

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
