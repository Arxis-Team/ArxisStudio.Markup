# 4. Where the loader stops, and two names it does not use

Date: 2026-07-31
Status: Accepted

## Context

Milestone 6 adds the Avalonia dependency and the first code that creates objects. Three decisions
were made while building it that are worth recording, because each departs slightly from the
contract's letter while following its intent.

## Decisions

### The environment's source provider is `IMarkupSourceProvider`

The contract's §5 lists `IXamlSourceProvider` among the interfaces external environments arrive
through. `ArxisStudio.Markup` already defines `IMarkupSourceProvider`, whose signature is exactly
what the loader needs: a URI in, a source or nothing out.

A second interface with the same shape would be an abstraction layer with no concrete use, which
the contract's own coding standards forbid. `XamlLoadEnvironment.SourceProvider` is therefore
typed as `IMarkupSourceProvider`. If a loader-specific capability ever appears, a derived
interface can be introduced then, with something to justify it.

### Avalonia resolves types independently of `IXamlTypeResolver`

Objects are built by `AvaloniaRuntimeXamlLoader`, which the contract names as the API to use.
That loader does its own type resolution from `LocalAssembly` and the assemblies in the process;
it does not accept a resolver from a caller.

`IXamlTypeResolver` is therefore this library's own instrument, not a hook into Avalonia's. It
answers what a name in the document means — for diagnostics now, and for member classification
in milestone 7 — and it is deliberately kept honest about that in its documentation, so nobody
later assumes that registering a resolver changes how objects get built.

### Loader tests need a fourth kind of project

The contract's testing strategy requires the loader tests to prove that "a custom control from an
explicitly supplied assembly loads". A control declared inside the test assembly would prove
nothing: that assembly is loaded either way, so the test would pass with the assembly resolver
removed entirely.

`tests/ArxisStudio.Markup.Xaml.Loader.TestControls` is a support library, not a test project. To
keep the two apart, `tests/Directory.Build.props` now applies the test SDK only to projects whose
name ends in `.Tests`.

## Consequences

- The environment has one fewer interface than the contract sketches, and one fewer type for a
  caller to implement.
- A reader of `IXamlTypeResolver` is told plainly what it does and does not affect, which is the
  kind of thing that is expensive to discover by experiment.
- `tests/` now contains projects that are not test projects, and the suffix rule that separates
  them is stated where it is enforced.
- Milestone 8's `x:Class` work will use `RuntimeXamlLoaderDocument`'s root-instance constructor,
  which is already the reason `IXamlRootInstanceFactory` exists here without being called yet.
