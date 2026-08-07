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
- **A value is converted the way loading converts it, and a type with no way to read its own text
  cannot be set.** Attribute text goes through the member's `TypeConverter`, and where there is none
  through a public static `Parse` — which is how Avalonia types such as `Thickness` and
  `CornerRadius` are read, since they declare no converter. A member whose type offers neither is
  refused with a diagnostic rather than handed a string it would throw on.
- **There is no generic rollback, and the result says so rather than implying otherwise.** An
  update reports one of three outcomes. `RejectedCleanly` means no live object was written and the
  session is exactly as usable as it was; `RequiresNewSession` means writing had begun before it
  stopped, so the objects are part-way to a document the session never adopted. The second is not
  recoverable here: what ran was user code — setters, converters, collection mutations, control
  code — with side effects nothing can reverse on its behalf. The session marks itself, refuses
  every later mutation with `AXM3043`, and keeps the offered document as `PendingDocument` so a
  caller can build the replacement session from it.
- **A setter that throws costs the session, not the edit.** Everything that can be checked without
  running the object's own code is checked first — the element still has an object, the member
  exists and can be written, the text converts to something the member holds, the collection says
  whether it is read-only — and that is where clean refusals come from. Once a setter has actually
  been called and thrown, what it did before throwing is unknowable: assigning the field, raising a
  notification and setting a second property before failing a cross-check is all legal, and looking
  at the written property afterwards would not notice any of it. So an exception out of a live
  setter is `RequiresNewSession` even when it is the first change the update tried to make. The
  price is real: a control library whose setter throws makes the session unusable rather than the
  edit refused. The conversion check in front of it is what keeps a half-typed value from ever
  reaching a setter.
- **A write to a rebuilt copy is still clean whatever it does.** An object this update built and is
  about to discard has never been handed to anybody, so a failing setter on one costs nothing.
- **A refused rebuild can leave the objects part-way.** Every fragment is built before any object
  is touched, so a fragment that will not build refuses the update cleanly; but the replacements
  themselves are applied one after another, and one that fails after another has succeeded stops
  there and is reported as `RequiresNewSession`. Collections are the reason this is not obvious:
  moving content out of a rebuilt copy and into the original empties one before it fills the other,
  and a failure between the two is not a refusal however it is spelled. Where a collection refuses
  *before* it has lost anything — an items control reading `ItemsSource` is the usual one — that is
  told apart by counting, and stays a clean refusal.
- **Cancelling an update is only clean before it writes.** A token cancelled while the update is
  still comparing, projecting or building fragments leaves nothing touched. One cancelled after the
  writes have landed leaves the objects ahead of the document, so the session is marked as needing
  recreation and the `OperationCanceledException` is raised on top of that — the caller gets the
  cancellation it asked for and `State` says what it cost.
- **Duplicating carries `x:Key`.** The names inside a copy are taken out by default, because a name
  scope refuses a second `x:Name` and the copy would not load. A key is not a name and is left as
  written, so duplicating a keyed resource produces two entries under one key, which a resource
  dictionary refuses in the same way. Which key the copy should have is a question about the tool's
  naming, not about copying.
- **Wrapping and replacing do not reformat what they are given.** A multi-line wrapper or
  replacement arrives written as the caller wrote it; only the wrapped element is re-indented, by
  the step the document already uses. This matches insertion, which has always behaved this way.

## Members

- **An attached member exists only once its owner has been initialised.** `GetMembers` reads
  Avalonia's registry, and Avalonia registers an attached property in the static constructor of the
  type that declares it. Before anything has caused `Grid` to be initialised, `Grid.Row` is not a
  member of anything. The answer is therefore not cached, so a tool that resolves types as documents
  ask for them sees the list grow while it runs — but a list taken at startup is not the whole list.
- **A content collection that refuses to be written through is reported, not forced.** An items
  control whose items come from `ItemsSource` says exactly that when asked to take a child the
  document declares, and a rebuild of such an element is refused with a diagnostic rather than an
  exception. The document is left alone; what the objects show comes from the binding.
- **Only what a document could have declared is mapped.** A collection reached through the content
  member contributes its items to the map when they are part of the logical world; rows a binding
  put there are not, because no markup describes them and holding them would pin a bound
  collection for the life of the session.
- **Content is whatever `[Content]` says, and nothing else is.** Where unnamed children go is read
  from Avalonia's own attribute, so a control library's own content member works exactly as the
  framework's do. Types that take children another way — `Style`, `ControlTheme` and the rest of
  the `IAddChild` family — declare no content member, and `FindContent` says so rather than
  guessing. Updating a style is a reload of the style rather than a replacement inside it.
- **`TextBlock.Inlines` is a whitespace-significant collection.** Avalonia marks it, and it means
  the spaces between inline elements are part of what is rendered. Editing never reformats, so
  nothing here disturbs them; writing a document back with `XamlWriteMode.Format` would, and that
  mode exists for a caller who asked for it.
- **Which members are worth showing is not answered here.** A control has upwards of two hundred
  settable members. Listing them is the library's job; choosing among them is the tool's.
- **What is known about a type belongs to the environment that resolved it.** Descriptors are cached
  by `XamlLoadEnvironment.MemberResolver`, one per environment by default, so a tool that rebuilds
  the user's assemblies builds a new environment and starts clean. Sharing one resolver between
  environments shares the cache, including across a rebuild — which is the caller's decision to
  make, and the reason it is not the default. `XamlMemberResolver.Instance` is process-wide and is
  there for a caller with no environment.
- **A property registered both as an ordinary and as an attached property is listed once**, under
  its simple name. `KeyboardNavigation.IsTabStop` and `IsTabStop` are both valid XAML for the same
  property; a tool that needs the qualified spelling writes it itself.

## Everything else

- **A resource in a theme dictionary needs the variant stated.** Everything about the load is
  right: the theme dictionaries arrive keyed by real `ThemeVariant`s, and `ActualThemeVariant` on
  the loaded tree is whatever the document asked for. But the ambient overload of Avalonia's
  `TryFindResource(key, out value)` does not find such a resource on a tree this library loaded and
  nobody has shown, while `TryFindResource(key, element.ActualThemeVariant, out value)` finds it —
  same element, same moment. Why the ambient overload does not pick the element's own variant up
  has not been established, so nothing here works around it. A host that looks resources up on a
  loaded document should state the variant.
- **A stand-in cannot see a background change that only changes priority.**
  `XamlDesignSurface` shows a root's `Background` only when the document declared it, which it
  decides from the value's priority. A transition that changes the priority and leaves the effective
  value untouched — a document declaring locally the very brush instance a theme was already
  supplying, or clearing one — raises no property-changed notification, and Avalonia offers no
  observable of a value's priority, only the one-shot `GetDiagnostic`. So there is nothing to
  subscribe to and the surface keeps showing what it last decided until it is attached again.
- **No sandbox.** Loading a document runs constructors, setters, type converters, markup
  extensions and any custom control code the document reaches. A caller loading XAML it did not
  write is running code it did not write, and this library makes no attempt to prevent that.
- **No project system.** Assemblies, resources and source arrive through the environment's
  resolver interfaces and nowhere else. Nothing here reads a `.sln`, a `.csproj`,
  `project.assets.json` or a package cache, and nothing here will.
- **Avalonia thread affinity.** Parsing and text editing are free of it; creating and mutating
  objects is not, and calling from the wrong thread fails with `AXM3004` rather than corrupting
  state that would surface later and somewhere else. The asynchronous updates may be called from
  any thread and marshal through the environment's dispatcher themselves; the synchronous editing
  methods must be called from the thread that owns the objects.
- **One session mutates at a time, and the two kinds of caller are treated differently.** Every
  change to a session passes through one gate. `ApplyDocumentUpdateAsync` and
  `ApplySourceUpdateAsync` **queue, first in first out**, with the order fixed when the call is
  made rather than when a continuation is scheduled; each observes its cancellation token while it
  waits. `SetValue` and `SetXamlValue` **refuse** with `AXM3044` instead of waiting, because
  blocking a thread there could be blocking the very thread the running update is dispatching
  to — a deadlock rather than a delay — and they refuse while anyone is queued, not only while the
  gate is held. Disposal waits for an update already running rather than cutting it off. Nothing
  about *reading* a session is guarded, and nothing about it needs to be.
- **The gate is not a public abstraction.** A host that wants to order work across *several*
  sessions has to do that itself; what is guaranteed here is the order within one.
- **`ArxisStudio.Markup.Xaml` grants its internals to the benchmarks assembly** so that lexing
  can be measured separately from parsing, as the contract asks. Nothing else has access.
