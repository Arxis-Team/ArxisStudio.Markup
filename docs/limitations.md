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
- **A class the environment cannot give is left out, not stood in for.** A document whose `x:Class`
  does not resolve — the form has not been built — or is not what its root says is loaded as the
  element its root is written as, in either mode. The class's constructor does not run, its code is
  absent, and `GetRoot<TheClass>()` fails: what is shown is the markup. A host that wants the class
  waits for a build, which is a new environment and a new session.

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
  resolved against the resources in scope where the markup sits. The same holds the other way: a
  rebuilt part that reads a key it does not declare is moved out to the element that does, and where
  no element declares it but one above includes another file, out past the outermost such element —
  which file holds the key is not something the syntax says. Only a key written as text is followed;
  a key written as an expression (`{StaticResource {x:Static …}}`) is not, and the part is rebuilt
  where it stands.
- **A structural change at the root rebuilds the root's content in place.** The root object
  itself survives, because a session is built around it and the caller holds it — and so do the
  dictionaries and lists it writes as property elements, which are refilled from the rebuilt copy.
  A change to the root element's own type or `x:Class` needs a new session, and so does a changed
  property element on the root that holds a single value (`<Window.Background>`): a value cannot
  be moved across without losing the binding it may have been, and the root has no slot to be
  rebuilt into.
- **A file an include at the root reaches is followed as content only.** `ApplySourceUpdateAsync`
  rebuilds the root's content when an include sits straight inside it; a `StyleInclude` in
  `<Window.Styles>` is not re-read that way, because the root's own document did not change and
  there is nothing to compare its property elements against.
- **A rebuilt part's handlers are hooked up by reflection, and only to the event the object has.** A
  part is built on its own, with no instance whose methods its handlers name, so they are left out of
  it and hooked up to the session's root afterwards through the event's own accessor. An event written
  as attached on another element — `Button.Click="Saved"` on a panel — is a routed event the panel has
  no accessor for; it is reported with `AXM3045` and not hooked up, and the rest of the update stands.
  A handler the class does not have, and every handler when the session was loaded without its class,
  is left out of the part exactly as it was left out of the load.
- **What Avalonia records about where it built something does not say which text it built it from.**
  Its runtime loader names every text it is handed alike — the document, each part an update builds,
  the markup live population builds a placed control from — so a recorded position is read as one in
  the document only where it falls inside the element the walk is in. A control's own markup,
  populated into an instance the form placed, is therefore not paired with anything: the control is
  the form's element, and what its markup built inside it belongs to no element of the form.
- **Expressions are set in place only where a load would need nothing else.** A binding, a dynamic
  resource, a static member and a null are; a static resource, a converter, a compiled binding and an
  argument the session does not know rebuild the element. A rebuild is always right, and an in-place
  write that guessed would not be.
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
  A fragment is the exception, because it is lifted left-aligned on purpose: it is indented to the
  sibling it lands beside.
- **A fragment's prefix is renamed only where the syntax says it names a namespace.** Element and
  attribute names, markup extensions, values that are nothing but type names, attached properties in
  parentheses and style selectors are reached; a value that only mentions the prefix among other
  words — `Text="see local:Badge"` — is left as written and reported with `AXM1044`. The rule cuts
  the other way once: a value that is *exactly* a prefixed name is taken to be one, so a `Text` that
  reads `local:Badge` and nothing else is renamed with the rest. Which members hold type names is a
  question about what they mean, and this package cannot ask it.
- **A prefix the receiving document does not use is declared as the fragment wrote it**, even where
  the document already has the namespace under another prefix. Two prefixes for one namespace is
  legal and costs one attribute; renaming rewrites the fragment, and is kept for when the prefix
  means something else here.
- **The default namespace is not reconciled.** A fragment whose unprefixed names are in a different
  default namespace from the one where it would land is refused with `AXM1043`: making it fit would
  mean prefixing every unprefixed name in it, markup extensions and type-name values included.
- **Relative URIs inside a fragment are not rebased.** An image's `Source="Assets/logo.png"`
  resolves against the receiving document's folder once inserted. `XamlFragment.SourceUri` says what
  it was relative to; which attributes hold a URI is, again, a question about members.
- **Names inside a template count against the whole document.** `XamlDuplicateNames.RemoveConflicting`
  takes out a fragment's name when the receiving document declares it anywhere, although a template
  is a name scope of its own — which errs towards taking out a name that could have stayed.
- **A declaration below a fragment's own element travels as written.** Avalonia refuses `xmlns` on
  anything but the root, in the fragment's source as much as in its destination, so markup that has
  one did not load where it came from either.
- **A member written as an element is found by its spelling's meaning, not by the property.**
  `SetPropertyElement` matches the namespace a prefix is bound to and the dotted local name, so
  `Grid.Resources` and `Panel.Resources` on a grid are two members to it — the package has no types to
  say they are one. Changing a member the document already writes takes the name it wrote it with.
  Where an element writes one member twice, the first is changed and the second left for the loader.
- **A live document's history is its own.** Each `XamlLiveDocument` keeps a history of one document,
  so a change that spans a form and the resource dictionary it reads is two steps in two histories.
  A host that needs one step keeps those documents in a `XamlWorkspace` of its own instead.
- **The history does not survive the process.** A live document restored in the next copy of a tool
  has its text and what is saved — it reads as changed — and nothing to undo.
- **A text that does not parse is never shown.** A live document whose text does not parse leaves the
  session showing the last text that did, as `Behind`; it is not built from a recovered parse, which
  would show something nobody wrote. Nor is a session built for a text whose update was refused when a
  load of it fails as well: the session in place stays and the document is `Behind` until the text
  loads.
- **A live document is moved by its file, not with its includes.** `RetargetAsync` builds the session
  again so that relative includes are found from the new place, but files the document includes that
  moved with it are the host's to follow.

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
- **A binding path is read as far as types go.** Dotted members, integer and string indexers and a
  leading `!` are followed; an attached property in parentheses, a cast, `$parent`, `#name`, the
  stream operator and a step typed `object` or resolved at run time are `NotUnderstood`. The path may
  well work — an indicator shows such a binding as not checked, never as broken.
- **A catalog lists what a document can name by itself.** Nested types, generic definitions, static
  classes, attributes and delegates are left out. A type's namespace is the first its own assembly
  maps the CLR namespace to — the mapping the loader's resolver reads — so a namespace mapped by a
  different assembly is not followed, and the type is listed under `using:`.

## Classification

- **The default resolver sees what the process has loaded.** `XamlLoadEnvironment.CreateDefault`
  finds Avalonia's own types among the assemblies already loaded, and remembers a failure for as
  long as the resolver lives. An Avalonia application has loaded them by the time it opens a
  document; a tool that classifies in a process of its own — a command-line indexer, a background
  worker — may not have, and gets `Unknown` for `<Window>` until it does. Load them before the
  first lookup (naming `typeof(Avalonia.Controls.Control)` is enough), or supply them:
  `CreateDefault(assemblies: [typeof(Control).Assembly])`.
- **A root of somebody's own type needs its assembly.** What `<local:ToolWindowBase>` is depends on
  what it derives from, and only its type says. Without the assembly the document is `Unknown`: a
  name ending in "Window" is not evidence. A templated control's look is the exception, because its
  root is Avalonia's own and the question is answered by the namespace a name is written in.
- **"Of your own" means "outside Avalonia's namespace".** A control library that declares its types
  into `https://github.com/avaloniaui` looks like Avalonia to the classifier, and its look files
  classify as styles; a third-party library with a namespace of its own looks like the author's, and
  re-templating one of its controls classifies as a templated control's look. The namespace is what
  the document says and what holds before a build; an assembly name would be a guess about
  packaging.
- **Selectors are read, not parsed.** The reader takes the type of each alternative's last step and
  passes over the rest, so a selector Avalonia would refuse can still yield a target, and one
  computed by a markup extension yields none. Nothing here reports a malformed selector — Avalonia
  does when it loads one.
- **`Template` is recognised by name.** A setter whose property is `Template`, however qualified —
  `TemplatedControl.Template`, `(TemplatedControl.Template)` — counts. When the control resolves it
  must derive from `TemplatedControl`; when it does not, the name is taken at its word.
- **`BasedOn` is followed inside the document only.** A control theme based on
  `{StaticResource key}` takes the template of the theme filed under that key in the same file —
  a text key, or an `{x:Type}` compared by the type it names. A key from another file, a merged
  dictionary or the application is not looked up, so a theme that only says what it is based on
  templates nothing here.

## Everything else

- **Converting a value from text leaves a trace in the process.** `XamlValueConversion` reaches a
  type's converter through `System.ComponentModel.TypeDescriptor`, whose cache is per-type and
  lives as long as the process — so a host that loads a generation of types, unloads it, and
  expects the assembly to be collected has to call `TypeDescriptor.Refresh(assembly)` for it.
  Nothing here can do that on the host's behalf: this library never learns that a load context
  exists, which is the point of `IXamlCompilationScope` (ADR 0013). `ArxisStudio.ProjectSystem`'s
  adapter is the reference implementation, and its ADR 0023 records what else such a host has to
  release before an unload actually takes.
- **A resource in a theme dictionary needs the variant stated.** Everything about the load is
  right: the theme dictionaries arrive keyed by real `ThemeVariant`s, and `ActualThemeVariant` on
  the loaded tree is whatever the document asked for. But the ambient overload of Avalonia's
  `TryFindResource(key, out value)` does not find such a resource on a tree this library loaded and
  nobody has shown, while `TryFindResource(key, element.ActualThemeVariant, out value)` finds it —
  same element, same moment. Why the ambient overload does not pick the element's own variant up
  has not been established, so nothing here works around it. A host that looks resources up on a
  loaded document should state the variant.
- **An object's origin is what the walk could establish, not a guarantee.** The map claims
  `XamlObjectOrigin.Document` only where it found a declaration for an object, so a control's own
  label — the one a `ContentPresenter` builds out of string content, for which Avalonia records no
  templated parent — reads as run-time generated rather than as something the document wrote. It is
  still not `Template`: nothing available says which template produced it. A caller asking "may the
  user edit this?" gets a better answer from `GetElement`, which is `null` for anything the document
  does not name.
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
