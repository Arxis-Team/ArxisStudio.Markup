# 16. A document is classified by its root, and by the templates it sets

Date: 2026-10-02
Status: Accepted

## Context

A designer opening a project's `.axaml` files has to decide, file by file, what it is looking at: a
window is shown through a stand-in, a user control or any other control as itself, a set of styles
or a dictionary through its `Design.PreviewWith`, and a templated control's look through an instance
of the control it templates. Every part of that answer was in these packages — the root element, its
namespace, a resolver that turns the two into a type — and the answer itself was not, so each host
would have derived it again, and each would have got the hard case wrong in its own way.

The hard case is the templated control. Avalonia's template for one writes its look as

```xml
<Styles xmlns:controls="using:App.Controls">
  <Design.PreviewWith><controls:TemplatedControl1 /></Design.PreviewWith>
  <Style Selector="controls|TemplatedControl1">
    <Setter Property="Template">…</Setter>
  </Style>
</Styles>
```

or as a `ControlTheme` in a `ResourceDictionary`. Its root is the same `Styles` or
`ResourceDictionary` as any theme's, so the root cannot say what the file is for. What says it is the
template it sets and whose control that is — a name inside a selector, which nothing here read, or
inside a `TargetType`.

Two shortcuts were on the table and both were rejected. Judging every root by its name would call
`<local:ToolWindowBase>` a window because of how it is spelled, and `<local:Shell>` nothing at all.
Judging "the author's own control" by assembly — not one of Avalonia's — would make the answer depend
on the build: before the project is compiled there is no assembly to ask, and a project's look files
are exactly what a designer opens first.

## Decision

**The syntax half lives in `ArxisStudio.Markup.Xaml`.** `XamlStyleAnalyzer` reads every `Style` and
`ControlTheme` as include discovery reads includes: by local name, wherever they appear, with no type
resolved. A declaration's targets are its `TargetType`, or the last step of each selector alternative
— the step the setters land on — with `^` standing for the parent's. The selector is read, not
parsed: the reader takes what a "whose style is this" question needs and passes over the rest, never
throws and never reports, because a wrong selector is Avalonia's to diagnose. Nothing in it
classifies a property or resolves a type, which is what the package's non-responsibilities forbid.

**The meaning lives in `ArxisStudio.Markup.Xaml.Loader`.** `XamlDocumentClassifier` resolves the
root's type through the environment and takes the first of application, window, user control,
control, styles, resource dictionary that it is or derives from. The `x:Class` is not consulted: it
derives from the root element's type and cannot change which of these it is. Only a set of styles or a
dictionary is looked into further.

**"Of the author's own" is a namespace, not an assembly.** A template set on a control written
outside `https://github.com/avaloniaui` makes the file a templated control's look; one set on
Avalonia's `Button` is a theme. The namespace is what the document says, so the answer is the same
before and after a build. When the control resolves it must be a `TemplatedControl`; when it does
not, the kind still stands, `IsResolved` says it rests on names, and a warning sits on the name.

**A template is followed through `BasedOn`, inside the document.** A theme library writes the
template once, in a base theme for Avalonia's own control, and a theme per control of its own that
only names its base: `BasedOn="{StaticResource AxButtonBase}"`. Reading setters alone called the
whole look of `AxButton` a dictionary. The chain is followed by `x:Key` within the same document —
text keys as written, `{x:Type}` keys by the type they name — and stops at a cycle or at a key the
document does not file; a base from elsewhere supplies a template this file does not.

**A root is never guessed.** A root the environment cannot resolve is `Unknown`, with an error on its
name. A namespace is reliable evidence; a type name is not.

## Consequences

- A host asks one question per file and gets the kind, the root type, whether the root is the
  author's, the templated controls, the preview element and what could not be resolved. Classifying
  creates nothing, needs no Avalonia thread, and costs a cached lookup per distinct type.
- A library that declares its controls into Avalonia's namespace is taken for Avalonia, and its look
  files classify as styles; one with a namespace of its own is taken for the author's. Both are
  recorded in `docs/limitations.md`.
- The answer is only as good as the environment's resolver. The default one finds Avalonia's own
  types among the assemblies the process has loaded and caches a failure: an Avalonia application
  has loaded them before it opens anything, while a bare process classifying the studio's own
  repository got `Unknown` for 37 of its windows, user controls and applications until it loaded
  `Avalonia.Controls`. That is the resolver's contract — already loaded assemblies — and is
  recorded as a limitation rather than worked around in the classifier.
- The selector reader is a second reading of Avalonia's grammar, deliberately partial. Its name rule
  is Avalonia's own, so a step it takes for a type is one Avalonia takes for a type; anything beyond
  "which type does the last step name" is out of its reach by design, and growing it into a parser is
  a decision for a later milestone, not a drift.
