# 2. Build against Avalonia 12.1.1

Date: 2026-07-31
Status: Accepted

## Context

The contract targets Avalonia XAML compatibility and requires the loader to use public Avalonia APIs — `AvaloniaRuntimeXamlLoader`, `RuntimeXamlLoaderDocument` — rather than XamlX internals.

Two lines were available: 11.3.18 (mature, most community examples and documentation) and 12.1.1 (current stable). Avalonia 12 has already renamed parts of the public test surface relative to 11.x, which is a signal that other public APIs the loader depends on may also differ between the lines.

## Decision

Pin Avalonia 12.1.1 in `Directory.Packages.props`, applying to `Avalonia`, `Avalonia.Markup.Xaml.Loader`, `Avalonia.Headless` and `Avalonia.Headless.XUnit`.

## Consequences

- **The test framework is decided by this.** `Avalonia.Headless.XUnit` 12.1.1 depends on `xunit.v3.extensibility.core` 3.2.2, so the repository uses xunit.v3, not xunit v2. This was not an independent choice.
- The headless test attribute is `[AvaloniaFact]`/`[AvaloniaTheory]` (`Avalonia.Headless.XUnit`), not the `[AvaloniaTest]` familiar from Avalonia 11 samples. Documentation and blog posts written against 11.x will not transfer verbatim.
- Community examples and Avalonia documentation still predominantly target 11.x. Expect to verify API shapes against the 12.1.1 assemblies rather than trusting search results — the milestone 6 loader work is where this cost is paid.
- Avalonia is referenced by `ArxisStudio.Markup.Xaml.Loader` only. `PackageBoundaryTests` enforces that the two lower packages stay free of it, both declaratively and in compiled metadata.
- Moving to a different Avalonia line later is a single edit in `Directory.Packages.props` plus whatever public-API drift the loader has to absorb.
