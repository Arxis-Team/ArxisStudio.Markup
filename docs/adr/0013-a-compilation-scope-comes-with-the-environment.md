# 13. A compilation scope comes with the environment

Date: 2026-08-10
Status: Accepted

## Context

The loader compiles markup at runtime through Avalonia, and Avalonia's runtime compiler keeps one
set of emitted state for the whole process: `AvaloniaXamlIlRuntimeCompiler` holds a static
`SreTypeSystem`, whose constructor snapshots every assembly the process has loaded and whose
`FindAssembly` answers a simple name with the *first* match it took, and a static dynamic assembly
whose generated code resolves those names again when it runs. None of it is resettable through
public API, and none of it is wrong for the application the compiler was written for — an
application's assemblies load once.

A host built on this library is not always that application. A designer loads documents against a
project's own build output, in a collectible `AssemblyLoadContext` per build, precisely so that a
rebuilt assembly is a *different* assembly. The two designs meet badly, and the failure was found
the hard way in `ArxisStudio.ProjectSystem`'s FormsDesigner: create a form, build, load — the
environment resolves the new assembly, the compiler's cache still answers with the first
generation, and object creation fails with `Could not load type 'App.NewWindow' from assembly
'App'` while an `Activator.CreateInstance` on the very same type object succeeds. A first-chance
stack trace pinned the throw inside the compiler's `LoadOrPopulate`.

The session cannot fix this. It does not know that load contexts exist, and this library's rules
forbid it to learn: which context an assembly lives in is the environment owner's knowledge,
exactly like which file an `avares` URI names (ADR 0009 puts member resolution with the
environment for the same reason).

The host also cannot fix it well from outside. Bracketing every call into the session works — it
is how the failure was first closed — but it makes correctness depend on every caller remembering,
at every call site, forever. An update compiles markup exactly as a load does; a root instance's
constructor may compile markup of its own. A host that misses one site gets the stale-cache
failure back, rarely and unreproducibly.

## Decision

The environment names the fix, and the session applies it everywhere.

`XamlLoadEnvironment` gains an optional `IXamlCompilationScope`, with one member: `Enter()`,
returning what to dispose when the compilation is done. The session enters it around every
operation that runs the compiler or the document's own code — creating the `x:Class` instance,
loading the document's objects, and rebuilding a fragment for an update — and disposes it on the
same thread, failure or not.

What an implementation does inside the scope is its own business. The one in
`ArxisStudio.ProjectSystem`'s adapter moves the compiler's static state into the generation that
is loading and enters contextual reflection on its context, and records the fragility of that in
that repository's ADR 0020. This library defines only the seam: *when* compilation happens is the
session's knowledge, *where* its output must live is the environment's.

## Consequences

- A host with replaceable load contexts implements the scope once, on the object that owns the
  contexts, and every load and update is bracketed without any caller remembering anything.
- Hosts that load against the default context set nothing and pay nothing: the property is null
  and entering is skipped.
- The scope must nest and must be cheap, and that is part of its contract's documentation: it is
  entered on every load and every update, and an update can begin while a constructor is still
  inside its own entry.
- The session remains ignorant of load contexts, which keeps ADR 0003's promise reachable: a
  future out-of-process provider needs no change here, because the scope travels with the
  environment.
