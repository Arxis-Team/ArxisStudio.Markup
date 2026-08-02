# Updates

Bringing running objects in line with a document that changed underneath them, without compiling
anything and without recreating what did not have to be recreated.

## Applying a changed document

```csharp
XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(edited, token);

result.Applied;      // whether the objects were changed
result.Strategy;     // the largest thing it took
result.Changes;      // what was found, in document order
result.Diagnostics;
```

The comparison is over the syntax tree rather than the text, which is the point of having a
lossless one: reindenting a file, adding a comment or reflowing an attribute across two lines
changes every offset in it and changes nothing about the objects it describes. Those updates come
back as `Strategy.None` with no changes at all.

An update that cannot be applied leaves the objects exactly as they were, and the document that was
offered is kept rather than dropped — the usual reason an update fails is that the author is
halfway through typing it, and the next keystroke is the correction:

```csharp
if (!result.Applied && session.PendingDocument is { } refused)
{
    Show(result.Diagnostics, refused);
}
```

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
is built around it, so there is nowhere to put a new one.

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

// 3. Keep the two in agreement whatever happened.
if (result.Applied)
{
    await File.WriteAllTextAsync(path, session.Document.GetText(), token);
}
else
{
    workspace.Undo();      // the document goes back to what the objects still show
    Show(result.Diagnostics);
}
```

That last branch is the part worth copying. The document and the objects disagreeing is the one
state these packages exist to prevent, and a history holding an edit the tree never took would be
exactly that.

The showcase in `samples/ArxisStudio.Markup.Xaml.Loader.Sample` is this loop with a user interface
on it — a tree, a live preview, a property inspector, delete, duplicate, wrap, undo and redo, built
on the published API with nothing added to `src/`.
