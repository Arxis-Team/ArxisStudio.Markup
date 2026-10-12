# 33. A binding is checked against what the document declares

Date: 2026-10-13
Status: Accepted

## Context

A tool showing a binding wants to say whether it works: whether its path names something on what it reads,
and whether the build of the project would take it. ADR 0026 answered half of that — a path read against a
source type, `ResolveBindingPath` — and left the source to the caller: the data type in scope or the design
data, and nothing for a binding that reads anything else. Every host then has to work out on its own what
`#title.Text`, `$parent[Window].Title` or `{RelativeSource FindAncestor}` read, and where a compiled binding's
type comes from — and each of those has a way to be answered wrongly.

The obvious way is the live tree: ask the object for its name scope, its ancestors, its data context. In a
designer that tree is the host's. The root stands inside the host's controls, so the nearest `Window` above a
`UserControl` is the host's window, an ancestor's data context is the host's view model, and a name is looked
up in the host's scope — a binding that does nothing in the application reads as working, and one that works
reads as broken. The second way is the session's own reading of compiled bindings, and that is not the
build's: a designer loads forgivingly, with reflection bindings, while the project the document belongs to
compiles them and refuses a path its data type does not have.

## Decision

**`XamlLoadSession.CheckBindingAsync` reads a binding's source from its markup and finds it in the document.**

- `#name` and `ElementName` name an element of the document's name scope — a template's own inside a template.
  A name nothing has is a broken binding: the build refuses it where it compiles, and it shows nothing where
  it does not.
- `$self` and `RelativeSource Self` are the element; `$parent[T;n]` and `RelativeSource FindAncestor` are the
  `n`-th ancestor of type `T` in the document, and where the document has none — the window a form will stand
  in — the type `T` itself, which is what a build reads.
- A `Source`, a templated parent or a mode it does not know is `Other`, and its path is `NotUnderstood`.
- The data context is read as a build reads it. A compiled binding reads the nearest `x:DataType`, which a
  data template does not inherit; with none in scope the build refuses it. A binding the build does not
  compile reads what it reads when the form runs: the data the document gives the objects — the nearest data
  context that is not inherited, which is what an element writes or what the class or the design data sets on
  the root — and the data type in scope only where there is no such data. `x:DataType` is what a compiler
  checks against; a reflection binding under an element that hands its content other data reads that data,
  and reading the data type there would call a working binding broken. What the root inherits comes from its
  host and is not the document's.
- A binding on `DataContext` reads what its parent has: the element's own `x:DataType` is about the data it
  gives its content.

**Whether a binding compiles is the build's question, and the caller says what the build's default is.** The
nearest `x:CompileBindings` decides, `{CompiledBinding}` always compiles and `{ReflectionBinding}` never does;
where nothing is written, `compilesByDefault` — the project's own — does, not the session's options.

**The answer is a `XamlBindingCheck`:** the source, the path read against it, the status of that reading, the
step it stopped at and why, the types it found — to be read and let go, as ADR 0026 says — and `BreaksBuild`
for a binding a build compiles and finds missing.

## Consequences

- A host shows a binding's state without a tree of its own and without reading the host it stands in.
- The check reads the document as it is now, and the objects for what only they can say: the type an element
  built and the data the document gave it. An element inside a template built nothing, and its type is the one
  its name resolves to.
- What it cannot follow is said to be not understood, never broken; a converter's result, a `Source`, a cast
  or a step typed `object` stay open questions.
