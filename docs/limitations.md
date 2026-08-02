# Known limitations

What these packages do not do, or do only partly, as of the first preview. Everything here is
deliberate and tested; nothing here is a bug report. Where a limitation exists because of
something outside this repository, that is said plainly.

The three rules in `README.md` are never traded away for any of it: the document stays the source
of truth, an unchanged document round-trips byte for byte, and unknown content survives.

What the packages *do* is documented in [`api/`](api/README.md).

## Includes

`ResourceInclude` and `StyleInclude` are resolved through `IXamlResourceResolver` by projecting
the document — see `docs/adr/0005-resource-includes.md` for why. That leaves four cases.

- **A prefix the host already binds elsewhere.** Avalonia's parser accepts `xmlns` only on a root
  element, so an included file's declarations are moved onto the root of the projected text. If
  the included file binds a prefix that the host binds to a different URI, the two cannot both be
  right once merged, and renaming one would mean rewriting every name and markup extension that
  uses it. The include is left as written and `AXM2011` says so.
- **`xmlns` on a non-root element of an included file.** Avalonia rejects it wherever it appears,
  so such a file does not load standalone either. It is spliced as written and Avalonia's own
  error is reported — against the included file, because the projection maps it there.
- **Relative URIs outside an include's `Source`.** A relative `Source` on an include that is
  being left as written is rewritten to the URI it already resolved to. A relative URI in any
  other attribute — an image source, say — cannot be found without CLR metadata about what that
  attribute means, and is not rebased. A fragment spliced from another folder can therefore carry
  a relative asset reference that resolves against the host's folder.
- **An include straight inside the root element** — a theme file is nothing else — is rebuilt as
  the root's *content*, since there is no slot to put a rebuilt root object into. The root object
  itself survives, so the session and whatever the caller is holding keep working.

## Design mode

- **Avalonia understands four names in the design namespace** — `d:DesignWidth`,
  `d:DesignHeight`, `d:DataContext` and `d:PreviewWith` — and has no emitter for any other, so a
  single `d:Text` fails the whole document in both modes. Every other design attribute is
  therefore removed from the projected text and applied afterwards, which means it is applied as
  an ordinary property set: it cannot do anything a property set cannot.
- **A design value written as a markup extension** is evaluated by the load, so changing one is
  not something re-applying design values can do. Such a change is treated as a rebuild.
- **A design value is applied as a local value**, so a binding on the same property overwrites
  it as soon as the property's data context arrives. A host that supplies a data context in
  design mode gets the binding's value, not the design one — which is consistent with what a
  design value is for, since its purpose is to show a document that has no data context yet.
- **Elements in the design namespace** — `<d:Something>` — are not removed. They are unusual, the
  contract asks only about attributes, and Avalonia reports them clearly enough.
- **`mc:Ignorable`** is honoured for attributes, by namespace rather than by prefix. Ignorable
  elements are not removed, for the same reason.

## Updates

- **Reordering is followed only where something names the elements.** An element that declares an
  identity — `x:Name`, or `Name` where it means the same — is paired by it across a move, and the
  objects that already exist are moved within the collection holding them rather than rebuilt.
  Where no name decides — none declared, one declared twice, a child added or removed — pairing
  falls back to position, and a move then reads as changed values or a rebuild. Being cleverer
  than that risks giving a control the value of whatever used to sit in its place, which is the
  one outcome worth being slow to avoid.
- **A reorder needs a collection that holds exactly what the document declares.** The objects are
  moved through the collection's own `Move`, so nothing is detached and a control keeps what it
  was holding. A collection that also holds something no markup declared is one this cannot place
  the rest of afterwards, and the update is refused rather than guessed at.
- **A static resource rebuilds the element that declares the resources**, not the element that
  reads them. A reader built on its own has no dictionary to read, because a static reference is
  resolved against the resources in scope where the markup sits.
- **A structural change at the root rebuilds the root's content in place.** The root object
  itself survives, because a session is built around it and the caller holds it. A change to the
  root element's own type or `x:Class` needs a new session.
- **An object rebuilt below a structural change is paired with its element by shape, and
  everything that survived the change carries its element across by position.** Avalonia records
  where it built the root of a separately loaded text and nothing below it, so the objects inside
  a rebuilt fragment have no recorded position to read — and reading them as positions in the
  document attributed them to whatever element sat at that line. What is known instead is that
  the fragment was built from a particular element, so its children are that element's children
  in order, and that is what the pairing uses. Where the two sides stop having the same shape —
  a property element contributing a dictionary or a template rather than a logical child — the
  pairing stops descending, and what is below keeps whatever the map can work out for itself.

## Editing and history

- **Two directions of writing, and they do not mix.** `XamlLoadSession.SetValue` writes the object
  and the session's document in one operation; recording edits on a `XamlDocumentEditor` and
  applying them through `XamlWorkspace` writes the workspace's document and creates an undo entry.
  Using both on one document advances one and not the other. `docs/adr/0007` says which to use.
- **An editor is bound to the text it was opened on.** Its edits are spans into that exact
  snapshot, so `XamlWorkspace.Apply` refuses an editor opened on a version the workspace has moved
  past, and refuses two editors for the same document. Record every edit to one document in one
  editor, and open a new one after each application.
- **Unwrapping cannot know what the slot will take.** Replacing an element with its children is a
  question about markup, and whether the member it sat in accepts more than one child is a question
  about what the member means, which the syntax package deliberately cannot answer. Unwrapping
  several children into a single-valued slot produces markup the loader reports when it builds it.
- **Wrapping an element the parent positions is not an in-place update.** A new parent written
  around an element reads to the difference as the same element with different attributes and a
  different child, which is the conservative reading and the correct one for everything else. Where
  the element carried attached properties — `Grid.Column`, `DockPanel.Dock` — the rebuilt object
  no longer has them and cannot be put back where it was, and the update is refused with `AXM3041`.
  Wrapping an element whose parent stacks or docks it works. A tool that needs the other case
  creates a new session from the edited document.
- **A refused rebuild can leave the objects part-way.** Every fragment is built before any object
  is touched, so a fragment that will not build refuses the update cleanly; but the replacements
  themselves are applied one after another, and one that fails after another has succeeded stops
  there. The document is left alone, so the two disagree until the caller reloads. Recreating the
  session is the only thing that certainly restores agreement.
- **Wrapping and replacing do not reformat what they are given.** A multi-line wrapper or
  replacement arrives written as the caller wrote it; only the wrapped element is re-indented, by
  the step the document already uses. This matches insertion, which has always behaved this way.

## Everything else

- **No sandbox.** Loading a document runs constructors, setters, type converters, markup
  extensions and any custom control code the document reaches. A caller loading XAML it did not
  write is running code it did not write, and this library makes no attempt to prevent that.
- **No project system.** Assemblies, resources and source arrive through the environment's
  resolver interfaces and nowhere else. Nothing here reads a `.sln`, a `.csproj`,
  `project.assets.json` or a package cache, and nothing here will.
- **Avalonia thread affinity.** Parsing and text editing are free of it; creating and mutating
  objects is not, and calling from the wrong thread fails with `AXM3004` rather than corrupting
  state that would surface later and somewhere else.
- **`ArxisStudio.Markup.Xaml` grants its internals to the benchmarks assembly** so that lexing
  can be measured separately from parsing, as the contract asks. Nothing else has access.
