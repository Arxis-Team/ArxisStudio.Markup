# 15. A session populates an x:Class root inside its constructor

Date: 2026-10-01
Status: Accepted. Extends [0014](0014-instances-of-compiled-controls-populate-from-live-documents.md)

## Context

A document with `x:Class` is loaded by creating the class and handing the instance to Avalonia to
populate. Every class a project writes calls `InitializeComponent()` from its constructor, and that
loads markup too — the markup compiled into the assembly, or, since ADR 0014, a live document
registered for the type. So a session populated its root twice, and `DefaultXamlRootInstanceFactory`
said so in its remarks and left it to the caller.

The cost was not cosmetic. What a root *accumulates* came out doubled: every style twice, every
handler attached twice. A keyed resource on the root was added under a key the dictionary already
held, which throws, and the whole load failed — "An item with the same key has already been added".
The designer sample in ArxisStudio.Surface met it on the first form with `<Window.Resources>`, and it
met it twice over, because it registers the form's own document for live population before opening
the form. And the fields `InitializeComponent` assigns from the name scope pointed at controls the
second population then replaced, so a form's own code talked to objects that were never shown.

`IXamlRootInstanceFactory` was offered as the way out, and it is not one. A factory can only stop the
constructor's load — and the hook it would need for that is the generated member ADR 0014 is the
only sanctioned reach into — which leaves the name-scope fields null and every constructor that uses
a named control after `InitializeComponent()` throwing. Only the session has what the constructor's
load should have been given: the projected text, the mode, the diagnostics.

Avalonia's own runtime loader, asked to create an `x:Class` root itself, does not populate twice: it
installs a populate into `!XamlIlPopulateOverride` for the length of the constructor and lets the
constructor's own load run the document. That is the answer, and the session cannot simply delegate
to it: it needs the caller's factory to construct the instance, and Avalonia's version writes `null`
into the field afterwards, which would silently end a live registration for the same type.

## Decision

The session lends the root type's populate hook the session's document for the length of one
construction (`XamlRootPopulation`). The first instance of the type constructed on the owning thread
while the hook is on loan is populated by the session — its projection, its design mode, its
diagnostics. Whatever was installed before is put back afterwards, and answers in the meantime for
every other instance: a copy of the control the document places inside itself, or one another thread
constructs.

A constructor that loads nothing, a type with no compiled markup, and a factory that hands over an
instance it did not construct here never call the hook; the instance is populated afterwards, as
before. The attribute checks and the projection move ahead of construction, since the text has to
exist before the constructor runs, and the handlers are checked against the class the document names
rather than the instance's runtime type — which is the type Avalonia compiles them against anyway.

The generated members are found and shape-checked in one place, `XamlPopulateHook`, which
`XamlLivePopulation` now uses too.

## Consequences

- An `x:Class` root is populated once, from the document, and its constructor's code after
  `InitializeComponent()` runs against the tree that is shown. Root resources, styles and handlers
  are no longer doubled.
- A live registration for the root's own type survives the session, and the session's document wins
  for the root — the registration is for *placed* instances, which is what ADR 0014 was about.
- A document that fails to build fails inside somebody's constructor. It is reported exactly as
  before (`AXM3002`, then `AXM3003`), and a constructor that then throws on a missing named control
  adds its own diagnostic; the session does not ask Avalonia to construct the class a second time.
- A future Avalonia without the hook degrades to the old behaviour — populate after construction —
  and nothing else changes. No public API was added; `IXamlRootInstanceFactory` remains for a caller
  that wants to construct its roots some other way.
