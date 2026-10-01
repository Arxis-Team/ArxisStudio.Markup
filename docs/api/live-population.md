# Live population

`ArxisStudio.Markup.Xaml.Loader` · `XamlLivePopulation`, `XamlLivePopulationResult`,
`XamlLivePopulationFailedEventArgs`

A session keeps *its own* document current on screen. This page is about everybody else's: a
control placed inside a document — `<views:MyControl />` — is constructed by Avalonia, and its
constructor loads the markup that was **compiled** into its assembly. Left alone, an embedded
control shows the shape its project had when it was last built, however current the document
placing it is kept.

`XamlLivePopulation` changes where that markup comes from. Avalonia's XAML compiler emits, into
every `x:Class` type, an override hook its generated `InitializeComponent` consults before the
compiled markup — the seam its own hot-reload tooling stands on. Registering a document installs
a populate there built from the document as it is now:

```csharp
using var population = new XamlLivePopulation(environment);

// Whenever MyControl.axaml is opened, edited, or reloaded from disk:
XamlLivePopulationResult result = await population.SetDocumentAsync(
    typeof(MyControl), document, cancellationToken);
```

From that point, **every new instance** of `MyControl` — constructed by a session loading another
document, by an update rebuilding a fragment, or by plain code — is populated from `document`
instead of the assembly. The one exception is the root of a session loading `MyControl.axaml`
itself: that instance is populated from the session's own document, once, and the registration
answers for everything else again as soon as it is constructed (ADR 0015). Unsaved edits included, because the document is whatever the caller
registered. ADR 0014 records why reaching a generated member is acceptable here and nowhere else.

## What registration does and does not do

`SetDocumentAsync` prepares the document the way a load would — attribute checks against the
type, includes resolved through the environment, design-time attributes stripped — and the
result's diagnostics are that preparation's. Registering again replaces the document; `Remove`
and `Dispose` put the compiled markup back.

It does **not** touch instances already on screen. Population happens at construction, so a
preview that already placed the control keeps its old instance until something rebuilds it. The
host decides what to rebuild, because the host knows what is showing; this registry only makes
the rebuild land on fresh content.

Three rules worth knowing before relying on it:

- **Population is runtime-faithful.** Never design mode: `d:DesignWidth` on an embedded control
  would size something that is not being previewed on its own. Design-time values belong to the
  session showing the document as a root.
- **The fallback is the compiled markup, reported through `PopulationFailed`.** A mid-edit
  document routinely does not compile, and the failure surfaces inside a constructor somebody
  else is running — so the instance falls back to what it would have shown anyway, and the event
  says so. The same fallback bottoms out cycles of live documents placing each other.
- **Dispose before the assemblies go.** The override field on a type roots the delegate, the
  delegate roots the registry, the registry roots the documents. On types in a collectible load
  context, an undisposed registry is a context that never collects. One registry per generation
  of assemblies, disposed just before it.

The one registration that is refused is a type with no compiled markup to stand in for —
`AXM3050 NotPopulatable`. A future Avalonia that stops emitting the hook degrades to exactly
that diagnostic, and the compiled behaviour remains.

## A designer's loop

```csharp
// On open and after every applied edit or disk reload of an x:Class document:
await population.SetDocumentAsync(controlType, form.Document, token);

// Then rebuild the open previews whose documents place that control; their fresh
// instances populate from the document registered above.
```

Which previews place which control is the host's knowledge — a scan of each document for
elements naming the class is enough for a designer, and `PopulationFailed` is worth wiring to
the same output the previews' own diagnostics go to.
