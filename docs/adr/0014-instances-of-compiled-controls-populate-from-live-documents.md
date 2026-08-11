# 14. Instances of compiled controls populate from live documents

Date: 2026-08-11
Status: Accepted

## Context

A control placed inside another document — `<views:MyControl />` — is constructed by Avalonia's
runtime loader, and its constructor loads the markup that was *compiled* into its assembly. None
of this library's seams sit on that path. `IXamlRootInstanceFactory` applies only to the root of
the document being loaded; `IXamlResourceResolver` is consulted only for `ResourceInclude` and
`StyleInclude`; type resolution inside a load is Avalonia's own (ADR 0004). So however current
the outer document is kept — every keystroke, every save — an embedded control keeps the shape
its project had when it was last built. A designer showing a form beside the control it places
shows two different versions of one file, and the honest answer it could offer, restarting after
every save, is exactly the workflow live editing exists to replace.

The one seam that reaches embedded controls is Avalonia's own, and it is not a public API. Its
XAML compiler emits into every `x:Class` type a private static field, `!XamlIlPopulateOverride`
of type `Action<object>`, and a trampoline that consults the field before running the compiled
populate. The field exists for precisely this purpose — it is what Avalonia's previewer and the
hot-reload tools around it stand on — but it is a generated member reached by name, and this
library's rules say public Avalonia API only, with any exception recorded in an ADR. This is that
ADR.

Two alternatives were considered and rejected. Substituting a shim type for the control at load
time breaks identity — `x:DataType`, styles and code that name the real type stop matching — and
requires intercepting Avalonia's type resolution, which ADR 0004 records as unavailable. Teaching
sessions to rewrite `<views:MyControl />` into the control's current markup inline would make the
outer *document* lie about what it contains, and would still miss instances created by code.

## Decision

The loader package gains `XamlLivePopulation`: a registry that installs, per compiled type, an
override populating new instances from a live `XamlDocument` instead of the compiled markup.

The contract has four load-bearing clauses:

- **Registration prepares; construction compiles.** Registering a document runs the same
  preparation a session load runs — attribute checks against the type, includes resolved through
  the environment, design-time attributes stripped — because that work is asynchronous and
  population happens inside a constructor, which is no place to wait. Construction itself runs
  one runtime compilation, inside the environment's `IXamlCompilationScope` (ADR 0013), on the
  constructing thread.
- **Population is runtime-faithful, never design-mode.** An embedded instance must behave the way
  its compiled markup would. `d:DesignWidth` applied to an embedded control is a layout bug, not
  a preview; design-time values belong to the session that shows a document as a root.
- **The fallback is the compiled markup, and it is reported, not thrown.** A live document
  mid-edit routinely does not compile, and the failure surfaces inside a constructor somebody
  else is running. The instance gets the compiled populate — stale beats blank, and blank beats a
  form that fails to open because of a control it merely places — and `PopulationFailed` says
  which happened. The same fallback bottoms out cycles, which only live documents can state: a
  project whose compiled markup contained one would never have built.
- **Registrations are owned.** The override field roots the delegate, the delegate roots the
  registry, the registry roots the documents — on a type in a collectible load context that chain
  is a leak with a user-visible name. Removing a type or disposing the registry puts the field
  back, and a field some other party has since claimed is left alone.

## Consequences

- A designer registers each open document for its `x:Class` type and rebuilds the previews that
  place it; every instance constructed from then on — in any session, however deeply nested —
  carries the document as it is now, unsaved edits included.
- The generated members are reached by name, with the same posture as the compiler reset recorded
  in `ArxisStudio.ProjectSystem`'s ADR 0020: both are checked for shape as well as name, and a
  future Avalonia that stops emitting them degrades to a diagnostic
  (`AXM3050 NotPopulatable`) and the compiled behaviour — stale, but not broken in any new way.
- Population changes constructions from now on, never instances already on screen. Deciding what
  to rebuild is the host's, because the host knows what is showing; this library only makes the
  rebuild land on fresh content.
- A second registry overriding the same type takes the field last-wins, which is the field's own
  semantics. One registry per generation of assemblies is the intended shape.
