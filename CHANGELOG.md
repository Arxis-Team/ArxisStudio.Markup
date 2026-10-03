# Changelog

What changed between versions of the three libraries, which are versioned together.

They are consumed by project reference and are not published to NuGet. Entries below
`0.2.0-preview.2` describe a time when they were, and are left as written: they are a record of
what happened, not a description of what is.

The three rules in [`README.md`](README.md) are never traded away for any of it: the document stays
the source of truth, an unchanged document round-trips byte for byte, and unknown content survives.

## Unreleased

### The first child of an empty element on a line of its own

`InsertElement` and `InsertFragment` put the first child of an element with no children straight after
its start tag, on the start tag's line: a designer dropping a button into an emptied user control wrote
`Height="420"><Button Content="Button" />`, with the blank lines below it. The first child now goes on
a line of its own, one step in — the step the file is written with, two spaces where it does not say,
and the document's own line break — whenever the element is laid out on lines: what it holds already
breaks a line, or its start tag begins a line in a document written on lines. Nothing already there
moves; the blank lines and comments the element held stay below the child, and an element that held
nothing, self-closing or not, gets its end tag back on a line at its own indentation. An element
written inline inside another, every element of a document written on one line, and an element that
holds text take the first child inline as before.

No public API changed.

### A live document

`XamlLiveDocument` is one document with its own history, the text last saved, and the session that
shows it. An edit, an undo and a redo are one step each, recorded in the text first and then shown: the
session follows in place where it can, a new one is built where it cannot (`SessionReplaced`, on the
owning thread, while the previous session is still open), and where nothing can be built the session in
place stays and the document is `Behind`, or `Broken` with nothing to show it. A text that does not
parse is never built from. Opening is not a step; whether the document is changed is a difference of
text against what was saved. Text from outside is a step under the host's description and is what is
saved — `ApplyIfClean` refuses over unsaved changes and reports the conflict, `TakeTheirs` and
`KeepMine` resolve it. `DetachAsync` lets go of the environment, the options and the session and keeps
the text and the history; `AttachAsync` shows the text in an environment; `RetargetAsync` follows a
renamed file. Operations run one at a time, in the order asked for.

`MarkupWorkspace.ChangeUri` moves a document to another URI without losing its history; the move is
not a step, and undo restores a step at the document's current URI.

`RebuildAsync` takes a chooser: it builds again only the elements chosen — those placing a control
whose own markup changed — in place, keeping the session, the root and every other object, and says
`XamlLiveDocumentChanges.Objects`; the root among them builds the whole text again. The elements are
chosen in the document's turn, from the text the session shows then.

Public API added: `XamlLiveDocument`, `XamlLiveDocumentState`, `XamlLiveDocumentChanges`,
`XamlLiveDocumentChangedEventArgs`, `XamlSessionReplacedEventArgs`, `XamlLiveEditResult`,
`XamlExternalTextPolicy`, `XamlExternalTextOutcome`, `XamlExternalTextResult`,
`MarkupWorkspace.ChangeUri`, `XamlLiveDocument.RebuildAsync(Func<XamlDocument, IEnumerable<XamlElement>>, CancellationToken)`,
`XamlLiveDocumentChanges.Objects`. `docs/adr/0025`.

### What a document can name, and what a binding reads

`XamlTypeCatalog` reads the types assemblies offer a document — the namespace a document writes each
in, the prefix the library suggests, and what kind of type it is — holding no type and no assembly.
`XamlLoadSession.GetDataContextAsync` says what the bindings written on an element read from: the
nearest `x:DataType`, the design data's type and whether bindings compile there.
`XamlMemberResolver.EnumerateBindable` lists what a binding can name on a source, by name, and
`ResolveBindingPath` reads a path against a type: resolved, broken at a step, or not understood —
never claimed broken beyond what types can say. An assembly some of whose types cannot be read is
reported with `UnreadableTypes` (`AXM2013`).

Public API added: `XamlTypeCatalog`, `XamlTypeEntry`, `XamlTypeKinds`, `XamlDataContextInfo`,
`XamlLoadSession.GetDataContextAsync`, `XamlBindableMember`, `XamlBindingPathResult`,
`XamlBindingPathStatus`, `XamlMemberResolver.EnumerateBindable`, `XamlMemberResolver.ResolveBindingPath`,
`XamlLoaderDiagnosticCodes.UnreadableTypes`. `docs/adr/0026`.

### Members written as elements, and paths written down

`XamlDocumentEditor.SetPropertyElement` sets a member written as a property element — replacing what an
existing one says and keeping its tags and the comments around the value, or writing a new one where
members go, laid out as the file is — and `RemovePropertyElement` takes one out; both on `XamlDocument`
as well. `XamlElementPath.Parse` and `TryParse` read back the text `ToString` writes.

Public API added: `XamlDocumentEditor.SetPropertyElement`, `XamlDocumentEditor.RemovePropertyElement`,
`XamlDocument.SetPropertyElement`, `XamlDocument.RemovePropertyElement`, `XamlElementPath.Parse`,
`XamlElementPath.TryParse`.

### Updates keep the class

A form whose `x:Class` resolved — the form a designer shows beside the IDE that edits it — met the
update path less well than one whose class was missing.

- Rebuilding the root's content projected the whole document, `x:Class` included, so Avalonia
  constructed the class a second time: the author's constructor ran again, and for a window a second
  platform window was left open, holding everything it was built from. The root's part is now
  projected without `x:Class`, `x:ClassModifier` and `x:Subclass`, its content moves onto the root from
  a copy of the element the root is written as, and a top-level copy is closed once its content has
  moved — and when the update refuses after building it: a single value the root writes as a property
  element, `Design.DataContext` among them, asks for a new session only once the copy exists, and that
  copy was left open, holding the design instance and every type it was built from. One that will not
  close is reported with `TopLevelCopyNotClosed` (`AXM3046`).
- A part holding a handler the class declares could not be rebuilt: a part is built without the
  instance whose methods its handlers name, and Avalonia refused the whole part. Handlers are now left
  out of every rebuilt part and hooked up to the session's root once the part's objects exist, through
  the event's accessor; one whose method cannot take the event's arguments is reported with
  `HandlerSignatureMismatch` (`AXM3045`, a warning) and the update applies without it. A handler
  written or renamed was treated as a value and refused as a member nobody can write; it now rebuilds
  its element, and on the root asks for a new session.
- A form whose class declares a private handler — code-behind's usual kind — did not load: a runtime
  load reaches a private member only of the assembly it names as the document's, and a session that
  was given no `LocalAssembly` named none. It now names the assembly of the class it populates, for
  the load and for every part it rebuilds.
- A part under `x:DataType` with compiled bindings did not compile: the nearest `x:DataType` and
  `x:CompileBindings` above a part are now written onto its root.
- A part that read a key with `{StaticResource}` from a dictionary above it was built with no value at
  all. It is now rebuilt together with the element that declares the key, or, where none does and an
  element above includes another file, past the outermost such element.
- Two rebuilds of one update, one inside the other, were both applied, and the map named the inner
  one's copy — a part the outer one had already replaced. The inner one is now left to the outer.
- A control with markup of its own, placed on the form and rebuilt as the root of its part, was handed
  to Avalonia to construct, which took the part for the control's definition: the control came back
  empty, and a live registration for its class ended. The session now constructs it, the way the
  document's load did, and loads the part onto it.
- Every write of an update, the map rebuilt over it, the design values applied again and the handlers
  hooked up now happen in one dispatcher turn. A cancellation can no longer arrive between the writes
  and the document they belong to.
- A window whose content was rebuilt — resources added at the root, say — lost the handlers in that
  content: a click on its rebuilt button ran nothing. The update paired the rebuilt elements with the
  root's logical children, and a window in Avalonia 12 has a host of its own among them beside its
  content, so the pairing stopped at the root and the map placed the new objects by positions recorded
  against the copy's text. The pairing now reads what the element's content member holds.

`docs/adr/0019`, `0020`, `0024`.

### Values and removals in place

An attribute taken out of an Avalonia property clears its local value where it stands
(`XamlUpdateStrategy.ClearProperty`), and an expression a load would evaluate with nothing but the
element to go on is set where it stands (`SetExpression`): `{x:Null}`, `{x:Static}`,
`{DynamicResource}`, and a `{Binding}` with `Path`, `Mode`, `StringFormat`, `ElementName`,
`RelativeSource`, `FallbackValue` and `TargetNullValue`, where bindings do not compile. Both were
structural, which at the root meant a new session — a new instance of the author's class for an
inspector's edit. Anything else, a CLR property's removal among it, rebuilds as before. A value an
update writes over a binding now ends the binding, as `SetValue` already did. An `mc:Ignorable` that
only gains namespaces nothing in the loaded document used is no change; the first design value a tool
writes declared and listed the design namespace, and that cost a new session.

**Breaking:** `ClearProperty` and `SetExpression` are inserted after `SetProperty`, because the enum is
ordered by cost and compared by that order. Every member from `UpdateDesignValue` on is two higher than
it was: `UpdateDesignValue` 4, `ReorderChildren` 5, `ReplaceResource` 6, `ReloadStyle` 7, `ReloadTheme`
8, `ReloadTemplate` 9, `ReloadSubtree` 10, `RecreateSession` 11. Code that stored the numbers has to
move with them.

`docs/adr/0021`.

### A host that borrows the root

`IXamlRootAccess`, carried by `XamlLoadOptions.RootAccess`, is lent the root around every write the
session makes to the tree — an update's whole turn, `SetValue`, the document side of `SetXamlValue` —
so a host that shows a window through a stand-in borrowing its content gives the content back for
exactly as long as a write takes. The map was rebuilt in a second turn and could walk a root whose
content was somewhere else.

Public API added: `IXamlRootAccess`, `XamlLoadOptions.RootAccess`. `docs/adr/0023`.

### Writing through a session, and what the map pairs

`SetValue` and `SetXamlValue` write an attached property as `Owner.Member`, declaring the owner's
namespace when the document lacks it, and find and read back an attribute written under any prefix;
an attached property was written under its bare name, which is another property. A synchronous edit
now keeps every pair the map had, including objects an earlier update rebuilt, and a source update
after one compares only what the includes contributed, so it no longer reads the edit as a change to
an include.

Avalonia names every text its runtime loader is handed alike, so where it recorded building something
does not say which text it read. A recorded position now pairs an object with an element only inside
the element the walk is in: a control placed on the form is the form's element, and what its own
markup — compiled or live — built inside it is nobody's. It mapped to nothing, so a designer could not
select or delete it.

`docs/adr/0022`.

### Rebuilding what the document did not change

`XamlLoadSession.ApplyRebuildAsync` builds named elements again as they stand, for a control whose own
markup changed while the form that places it did not: registered with `XamlLivePopulation`, the new
markup is what the rebuilt instances show. Each element is rebuilt the way a change to it would be; an
element of a document an update has since replaced is refused with `UpdateNotApplied`, and the root,
which the session is built around, with `RecreateSession`.

Public API added: `XamlLoadSession.ApplyRebuildAsync`. `docs/adr/0024`.

### A form nobody has built yet is still shown

A document whose `x:Class` names a class the environment does not have — the ordinary state of a form
created from a template, before the project's first build — produced no session at all:
`AXM3020`, then Avalonia's own "Unable to resolve type", then `AXM3003`. The loader reported the class
and meant to carry on without it, but the directive still went to Avalonia in the projected text, and
Avalonia resolves it on its own. It is withheld now, as a handler with nothing to hook up to already
was, and the root is built as the element it is written as — in both load modes. A class that is not
what the root says went the same way and was built as the class; it is built as the element too.

What the load withheld, every projection of the session now withholds: an update rebuilding the root's
content, a fragment rebuilt around a handler, and the reprojection a source update compares against the
load's. The last of those differed from the load's text for any document with a handler nobody had
written, so an include that had not changed could rebuild what it reaches — that is fixed with it.

Behaviour that changed: `UnresolvedRootType` (`AXM3020`) is a warning, because the load now goes on
without loss; `IncompatibleRootType` (`AXM3021`) stays an error. Updates report what the attribute
checks notice about the document offered — a handler still missing, for one. No public API changed.
`docs/adr/0017`.

### Names in namespaces the document has not declared

`XamlDocumentEditor.Qualify` names an element, or an attached property's owner, in a namespace at a
place: under the prefix the document gives it there, unprefixed in the default namespace, and otherwise
declared on the root — after the root's declarations and laid out like them, under the prefix asked for
or one made up from the namespace, numbered when the document declares or writes it already. Nothing is
ever declared as the default namespace. `QualifyAttribute` does the same for a namespaced attribute,
which is never unprefixed. Declaring the design namespace declares markup compatibility and lists it in
`mc:Ignorable` in the same edit, and `EnsureIgnorable` lists any other — added to an existing list, never
rewriting it, and completing an `mc:Ignorable` whose prefix was never declared rather than writing a
second one.

An insertion recorded where a replacement begins is now placed in front of it whichever was recorded
first. One order used to work and the other threw; it is what lets a declaration and the opening of a
self-closing root be one edit.

Public API added: `XamlDocumentEditor.Qualify`, `QualifyAttribute`, `EnsureIgnorable`.

### Markup from another document

`XamlFragment.From` lifts an element with the declarations its text uses written onto its own start
tag, the namespaces its source marked ignorable, and the indentation it sat at taken off; `ToXamlText`
and `XamlFragment.Parse` make it a clipboard format. `XamlDocumentEditor.InsertFragment` puts it into a
document, reconciling each namespace it uses: nothing where the prefix already means the same, a
declaration where the prefix is free, and a rename only where it means something else here — at every
place the syntax says names a namespace, with any other mention left as written and reported. The
inserted element leaves its declarations, its `mc:Ignorable` and its `x:Class` behind, is indented to
where it lands and is written with the document's line breaks. A fragment that is not one element, or
whose unprefixed names are in another default namespace, is refused with a diagnostic.

Names follow `XamlDuplicateNames.RemoveConflicting`, new and the default for a fragment: only the names
the receiving document already declares are taken out, so a control moved between forms keeps the name
its code behind uses.

Public API added: `XamlFragment`, `XamlDocumentEditor.InsertFragment`,
`XamlDuplicateNames.RemoveConflicting`, `XamlDiagnosticCodes.MalformedFragment` (`AXM1042`),
`FragmentDefaultNamespaceConflict` (`AXM1043`) and `FragmentPrefixLeftAsWritten` (`AXM1044`).
`docs/adr/0018`.

### A value written over a binding ends the binding

`XamlLoadSession.SetValue` over a property the document binds wrote the literal into the
document and onto the object, and reported the replacement — and left the binding running. A
binding the document applies runs at local-value priority, and setting a local value does not end
it, so the next change of its source wrote over the literal: the document said `Text="literal"`
and the object showed whatever the source said, with nobody told. The binding is ended now before
the value is set, and should the document then fail to take the edit, the session asks to be
recreated rather than claim the object is back as it was — nothing here can start the document's
binding again. `SetXamlValue` with a literal goes the same way. Found while the showcase was
made to write values through its session.

No public API changed.

### What a document is

`XamlDocumentClassifier.ClassifyAsync` says whether a document is an application, a window, a user
control, another control, a templated control's look, a set of styles or a resource dictionary —
by its root's type and that type's bases, through the environment's type resolver, without loading
anything. A templated control's look has `Styles` or `ResourceDictionary` at its root, so its root
cannot say what it is; a style or control theme in it that sets the `Template` of a control written
outside Avalonia's namespace does — itself, or through a theme in the same file it is `BasedOn`.
That is decided by the namespace, so the file classifies before the project is built, and the
control is checked against `TemplatedControl` when it resolves. The result says whether the kind
rests on types or on names, lists the templated controls, and hands over the `Design.PreviewWith`
element. A root nobody can resolve is `Unknown` rather than guessed. The showcase has a section for
it: a dozen documents, one of every kind, each previewed the way its kind calls for — and a switch
that takes the showcase's own assembly away, to show what a designer sees before a project's first
build.

Underneath, `XamlStyleAnalyzer` reads every `Style` and `ControlTheme` from the syntax alone: the
types each applies to — `TargetType`, or the last step of each selector alternative, with `^`
standing for the parent's — its setters and its nesting, with spans that stay right through entity
references.

Added to `ArxisStudio.Markup.Xaml`: `XamlStyleAnalyzer`, `XamlStyleDeclaration`, `XamlStyleKind`,
`XamlStyleSetter`, `XamlTypeReference`. Added to `ArxisStudio.Markup.Xaml.Loader`:
`XamlDocumentClassifier`, `XamlDocumentClassification`, `XamlDocumentKind`, `XamlTemplatedType`.
See [ADR 0016](docs/adr/0016-a-document-is-classified-by-its-root-and-by-the-templates-it-sets.md).

### An x:Class root is populated once, inside its constructor

A session created its root by constructing the class, and the class's constructor loaded markup of
its own — the compiled markup, or the document registered for live population — before the session
populated the same instance again. Everything the root accumulates came out doubled, and a keyed
resource on the root failed the whole load: "An item with the same key has already been added". The
session now lends the type's populate hook its document for the one construction, so the
constructor's own `InitializeComponent` is the population, and the fields it assigns point at the
controls that are shown. What was installed before — a live registration — is put back and keeps
answering for placed copies. The attribute checks and the projection now run before construction,
against the class the document names. See [ADR 0015](docs/adr/0015-a-session-populates-an-x-class-root-inside-its-constructor.md).

### An update carries what an element writes as property elements

Adding `<X.Resources>`, a style to `<X.Styles>` or a row to `<Grid.RowDefinitions>` reads as a change
to the element's content, and rebuilding the content moved the `[Content]` member across and threw
the rebuilt copy away with the rest — while reporting the update as applied. Changed dictionaries
and lists written as property elements are now moved onto the object that stays, a refilled
dictionary carries its theme dictionaries too, and the elements of its entries are paired by key.
A changed single-valued property element rebuilds the element's object instead, and at the root is
refused with `RecreateSession`. A string `Content` is no longer mistaken for a collection.

No public API changed.

### The stand-in for a window lives with the editor that shows it

Between releases this family had a fourth package, `ArxisStudio.Markup.Xaml.Design`, holding one
control: `XamlDesignSurface`, a stand-in for a root Avalonia will not let anything host. A `Window`
is a `TopLevel` and is parented at construction, so the object the loader correctly produces for
`MainWindow.axaml` cannot be shown; the control borrowed the window's content, resources and styles,
mirrored its background, size and theme variant, and carried its `DataContext` across.

It is gone from here, and no release ever contained it. All it took from the loader was the root
object and a thread check, and nested inside an editor's container it left the host three things to
keep in step. The same mechanics are the form container of ArxisStudio.Surface now —
`UiDesignerFormItem`, which takes `session.RootObject` as an object — so a host references that
library for it and nothing of this one. The architecture guards enumerate three packages again, and
the limitation about background priority left `docs/limitations.md` with the code it described. See
the note that closes
[ADR 0012](docs/adr/0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md).

No public API of the three remaining packages changed.

### A dispatcher can run asynchronous work, and loading stopped blocking on it

Creating a document's `x:Class` instance was one dispatched operation that did two unrelated
things, and waited for both on the owning thread. Resolving the class is the caller's
`IXamlTypeResolver` — genuinely asynchronous, permitted to read a file or ask another process or
marshal to the owning thread — and waiting for it *from* the owning thread is waiting for a thread
this call is sitting on. Creating the instance is the caller's `IXamlRootInstanceFactory`, which
may also be asynchronous, and which the session finished with `.GetAwaiter().GetResult()`.

Resolution now happens off the dispatcher, where it always belonged: it touches no Avalonia object
and compiles nothing, so it needs neither the thread nor the compilation scope. Creation stays on
the dispatcher — it runs a constructor that makes Avalonia objects — and goes through a new
`IXamlDispatcher.RunAsync`, which starts an asynchronous operation on the owning thread and resumes
it there rather than blocking the thread for its result.

**`IXamlDispatcher` has a second member.** A host that implements the interface itself must add
`RunAsync`; `AvaloniaXamlDispatcher` already has it. It is a second name rather than an overload of
`InvokeAsync` because a lambda returning a task satisfies both signatures, and which one it binds
to is decided by rules nobody reads at a call site — while the difference between them is whether
the task is awaited at all.

One thing this uncovered: a session was constructed wherever the load's continuation happened to
land, and constructing one builds the object map, which reads the source information Avalonia
records on each object — an attached property, with the thread affinity every one of them has. It
had always been the owning thread in practice, because nothing above it ever really suspended. Now
it is dispatched and says so.

## 0.2.0-preview.2

What a review of the previous release found. Four things, all of them cases where the code was
*nearly* right — which is why the tests written alongside it passed. No public API changed;
`XamlMutationGate` and `XamlObjectExposure` are internal. See
[ADR 0011](docs/adr/0011-what-a-review-found-in-the-mutation-boundary.md).

### An edit could race past a session that had already stopped accepting them

`SetValue` and `SetXamlValue` asked whether the session was still usable and *then* took the
mutation gate. Between those two moments an update that owned the gate could fail after writing,
mark the session and let go — and the edit would take its turn on a session that by then refused
everything. Both checks now happen with the gate held, and the answer read on the way in is not
consulted at all. Disposal is checked the same way, and its flag moved from a plain `bool` to an
`Interlocked` read, which is also what makes disposing twice safe rather than accidentally safe.

### A cancelled update could leave nowhere to recover to

`docs/api/updates.md` tells a caller whose update stopped part-way to build a new session from
`PendingDocument`, and the path that marked the session after a post-write failure did not set it.
It is now set wherever the session is marked, cancellation included; once set by the failure that
broke the session it is not replaced by a later refusal, and it is cleared only when a whole update
is adopted.

### An invoked setter that throws is never a clean refusal

**This reverses a judgement from the previous release.** It was treated as clean when it was the
first write of an update, on the reasoning that a setter refusing a value leaves the object as it
was. That is true of a property which validates before assigning — Avalonia's `validate` callback
is exactly that, which is what made it look general — and false of this, which any control library
may write:

```csharp
set
{
    _value = value;
    Tag = Describe(value);
    throw new InvalidOperationException("…and now I am unhappy.");
}
```

The rule is now that **a refusal has to be reached without running the object's own code**.
Everything checkable is still checked first and that is where clean refusals come from; past that
point a failure is `RequiresNewSession`, even as an update's first change. Two things keep this
from being ruinous: the conversion check means a half-typed value never reaches a setter, and
Avalonia reports `IsReadOnly` on an items control's `Items` once `ItemsSource` is bound, so the
case a designer meets daily is refused before anything is invoked. A write to a rebuilt copy the
session has never exposed is still clean whatever it does.

### Queued updates now really are a queue

`SemaphoreSlim` gives mutual exclusion and promises nothing about the order it releases waiters in,
while the documentation said updates queue. For a host watching files, three saves whose oldest is
released last means a preview showing text from two saves ago with every mutation perfectly
serialised. An internal FIFO gate replaces it: order fixed when the call is made, ownership handed
straight to the next waiter rather than dropped for whoever wakes first, a lock held only long
enough to move a link in a list and never across an `await`, a lease released exactly once, and a
non-blocking `TryEnter` for the synchronous edits which also refuses while anyone is *waiting*.
Cancelling a waiter removes that one turn and can never release ownership it did not hold.

### No longer published, and no CI of its own

The three libraries are referenced as projects rather than installed as packages, so everything
that existed to make a package went with the packaging: the licence expression, the packed readme
and licence file, the tags, the symbol package, the repository and Source Link metadata, and the
architecture test that guarded them. `dotnet pack` now produces nothing anywhere in the repository
rather than three packages nobody asked for. The GitHub Actions workflow is gone with them.

The `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` tracking went too, with the analyzer that
enforced it, the test that guarded the wiring and the tool that maintained the files. It existed to
make the published surface a promise; with nothing published there is no promise to keep, and the
ceremony cost more than it was worth. What a change adds to the public API is now visible in the
diff of the code, and nowhere else.

What stayed is what was never about distribution: warnings as errors, the architecture tests that
enforce the package boundaries, and XML documentation on public APIs, which a project reference
delivers to the IDE from the file beside the assembly.

### Migrating

Nothing to change unless you relied on a throwing setter leaving a session usable. If you did, that
was never safe; check `Outcome`/`State` and rebuild the session, as
[docs/api/updates.md](docs/api/updates.md#a-tools-update-loop) shows.

## 0.2.0-preview.1

Milestones 12 to 14, and the hardening a review asked for before anything is built on these
packages. One breaking change, described under *Migrating* below.

### An update says what it did, not just whether it worked

`XamlUpdateResult.Applied` was a boolean, and its documentation promised that an update either
lands whole or does not land at all. The implementation could not promise that for arbitrary user
code, and some of its diagnostics said the objects were untouched when they were not.

- **`XamlUpdateOutcome`** — `Applied`, `RejectedCleanly`, `RequiresNewSession`. The first two leave
  a session worth keeping; the third does not.
- **`XamlSessionState`** and **`XamlLoadSession.State`** — a session that stopped part-way through
  an update says so, and refuses every later mutation with `AXM3043` rather than writing onto a
  tree that describes nothing. Reading it still works.
- **`AXM3043`** `SessionRequiresRecreation` and **`AXM3044`** `SessionBusy` are new.
- `PendingDocument` is now also what a replacement session should be built from, not only what was
  refused.
- Every path that reaches a live object — property writes, reorders, dictionary and list
  replacement, content moves, fragment rebuilds, design-value reapplication, cancellation — was
  audited and classified. Collections drove most of it: moving content out of a rebuilt copy and
  into the original empties one before it fills the other, and a failure in between is not a
  refusal. Where a collection refuses *before* losing anything — an items control reading
  `ItemsSource` — that is told apart by counting, and stays a clean refusal.
- Several reflection and indexer writes that could throw out of `ApplyDocumentUpdateAsync` are now
  reported as diagnostics, which is what the error-handling policy asks for.

### One session mutates at a time

`XamlLoadSession` had asynchronous update paths that could overlap while reading and replacing
`Document`, `Projection`, `Objects`, `PendingDocument` and the object tree itself — which is what a
host watching a folder gets when a form and its dictionary are saved together.

- Every mutating operation now passes through one gate per session.
- `ApplyDocumentUpdateAsync` and `ApplySourceUpdateAsync` **queue**, observing their cancellation
  token while they wait. The gate is released in a `finally` on success, failure and cancellation.
- `SetValue` and `SetXamlValue` **refuse** with `AXM3044` rather than wait: blocking there could
  block the thread the running update is dispatching to, which is a deadlock rather than a delay.
- `DisposeAsync` waits for a mutation already in flight instead of cutting it off, and is now
  genuinely asynchronous. Work arriving afterwards gets `ObjectDisposedException`.
- Reading — the object map, `GetMembers`, `GetValueInfo` — takes no lock.

### Fixed

- **Adopting an updated document touched Avalonia objects off the owning thread.** Rebuilding the
  object map reads what Avalonia recorded on the objects themselves, and it ran on whatever thread
  the update happened to resume on. That worked for as long as every caller updated from the UI
  thread and failed the moment one did what the asynchronous API invites — call it from a file
  watcher. It now runs on the dispatcher, like everything else that reaches an object.

### From milestones 12 to 14, first released here

- **Identity and reordering** — an element that declares `x:Name`, or `Name` where it means the
  same, is paired across an update by it, and reordered siblings move rather than being rebuilt.
- **`XamlWorkspace`** — structured edits applied through the workspace, so one edit is one undo
  entry under a name a user would recognise, and an edit spanning two documents is one action.
- **`ReplaceElement`, `WrapElement`, `UnwrapElement`, `DuplicateElement`.**
- **`XamlElementPath`** — a reference to an element that survives an edit, an undo and a redo.
- **`XamlElement.ContentElements`, `MemberElements`, `Identity`, `IndexInContent`** — the rules
  every host was re-deriving, published. An insertion index counts content children, so index 0 in
  a parent that declares a property element means before its first content child.
- **`XamlLoadSession.GetMembers`, `XamlMemberResolver.Enumerate` and `FindContent`** — a tool can
  ask a type what it has instead of keeping a table of names, and where unnamed children go is read
  from Avalonia's `[Content]` attribute rather than from a list of framework base types.
- **`XamlMemberDescriptor.ConvertFromText`** and `XamlValueConversionResult` — the same conversion
  a write does, askable before anything is written.
- **`XamlLoadEnvironment.MemberResolver`** — descriptors are cached per environment rather than per
  process, so rebuilding a control library and loading it again starts clean.

### Migrating

**`XamlUpdateResult.Applied` is no longer settable.** It is now derived from `Outcome`, so the two
cannot disagree. Nothing outside these packages constructs an `XamlUpdateResult`, so this affects
only code that did:

```csharp
// before
new XamlUpdateResult { Applied = true, Strategy = …, Changes = …, Diagnostics = … }

// after
new XamlUpdateResult { Outcome = XamlUpdateOutcome.Applied, Strategy = …, Changes = …, Diagnostics = … }
```

**Reading `Applied` still compiles and still means what it meant.** But a tool that treats every
`Applied == false` as "undo the document and carry on" now has a case to add: after
`RequiresNewSession` the objects have moved and undoing the document is a lie. The three-way form
is in [`docs/api/updates.md`](docs/api/updates.md#a-tools-update-loop).

**`DisposeAsync` may now actually await.** It waits for a mutation in flight. Code that dropped the
returned `ValueTask` was already wrong and is now wrong visibly.

**Packages carry Source Link and symbols.** Nothing to do; stepping into these packages now lands
in the commit they were built from.

## 0.1.0-preview

Milestones 0 to 11: the text model, transactions and undo, the lossless lexer and parser, values
and editing, the resource graph, the Avalonia loader, runtime mapping and properties, `x:Class` and
events, resources, styles and templates, design mode and updates, and stabilisation.

Every item under *Definition of done for the first preview release* in [`README.md`](README.md)
holds from this release onwards.
