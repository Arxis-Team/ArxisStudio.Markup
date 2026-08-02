# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## The contract

`README.md` at the repository root is the **architectural contract**, not a description. It defines three packages, their boundaries, 12 milestones, and an explicit out-of-scope list. Read the relevant section before changing anything, and report any required architectural deviation *before* implementing it.

Development proceeds one milestone at a time. Do not skip ahead; do not start runtime loading before the text model and lossless document model have passing tests.

## Build and test

```bash
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet pack -c Release            # three packages from src/

# one test project
dotnet test tests/ArxisStudio.Markup.Tests -c Release

# one test by name (xunit.v3)
dotnet test tests/ArxisStudio.Markup.Architecture.Tests -c Release --filter 'FullyQualifiedName~PackageBoundaryTests'
```

`global.json` pins SDK 10.0.101 — three SDKs are installed on this machine, so always invoke `dotnet` from the repository root.

## Architecture

Three packages, one allowed dependency direction. Circular dependencies are forbidden.

```
ArxisStudio.Markup              format-independent document infrastructure
        ↑
ArxisStudio.Markup.Xaml         lossless XAML syntax model + editing + serialization
        ↑
ArxisStudio.Markup.Xaml.Loader  live Avalonia objects, resolution, runtime sync
```

**`ArxisStudio.Markup`** — `SourceText`/`TextSpan`/`TextChange`, document identity and versions, source providers, diagnostics, `MarkupWorkspace`, transactions with undo/redo, a generic dependency graph. It must not parse XML or XAML, resolve CLR types, or touch Avalonia.

**`ArxisStudio.Markup.Xaml`** — lossless lexer and parser (tokens *and* trivia), syntax tree, namespaces and directives, markup-extension parser, structured edits, `Preserve`/`Format` serialization, resource/style include discovery. It must not instantiate objects, execute markup extensions or type converters, classify Avalonia properties, or reference Avalonia assemblies.

**`ArxisStudio.Markup.Xaml.Loader`** — `XamlLoadEnvironment`, assembly/type/resource resolvers, `XamlLoadSession`, node↔object mapping with origin tracking, member classification, controlled `SetValue`, `x:Class`, design mode, incremental document updates.

The boundaries in the two paragraphs above are enforced mechanically by `tests/ArxisStudio.Markup.Architecture.Tests` — both from the `.csproj` graph and from compiled assembly metadata. If a change makes those tests fail, the change is in the wrong package; move the code, do not relax the test.

### The three rules that shape every design decision

1. **The source document is the source of truth.** Never regenerate XAML from a runtime object tree. `Text="{Binding Customer.Name}"` must never be written back as `Text="Alice"` just because that is the effective value.
2. **Round-trip preservation is a hard requirement.** An unchanged document must round-trip byte-for-byte. A single edit must leave comments, blank lines, indentation, attribute order, prefixes, quote style, and unknown content untouched.
3. **Unknown content survives.** An unrecognised element, attribute, namespace, directive, or markup extension may raise a diagnostic, but must never be discarded or rewritten.

### Naming

Public types use `Xaml`, never `Axaml`/`AXaml`/`AXAML` — following Avalonia's own terminology (`Avalonia.Markup.Xaml`, `AvaloniaXamlLoader`). The `.axaml` file extension is unrelated to type naming. `PackageBoundaryTests` enforces this.

### Error handling

Ordinary user errors (bad syntax, unresolved type, missing resource) produce `MarkupDiagnostic` values with stable machine-readable codes and source spans — never exceptions, and never error categories derived from parsing exception strings. Reserve exceptions for invalid API use, disposed sessions, broken internal invariants, cancellation, and unrecoverable runtime failures. Prefer result models such as `XamlLoadResult`. A failed transaction must never leave a partially mutated document.

### Threading

Parsing and text editing are UI-thread independent. Avalonia object creation and mutation respect Avalonia thread affinity — fail clearly on the wrong thread or take an injected dispatcher. Async APIs accept a `CancellationToken`. Never block with `.Result` or `.Wait()`.

## Hard boundaries

Never add to these packages: MSBuild evaluation, `.sln`/`.csproj`/`project.assets.json` reading, NuGet search or restore, C# compilation, Roslyn analysis, IDE integration, a visual designer, selection adorners, property-inspector UI, drag and drop, pointer/keyboard interception, or a sandbox for untrusted XAML. This governs `src/`; a sample may demonstrate what a host builds on the published API — the showcase has a property inspector for exactly that reason, recorded in `docs/adr/0006-inspector-in-the-sample.md`. External environments enter only through the resolver/provider interfaces (`IMarkupSourceProvider`, `IXamlAssemblyResolver`, `IXamlTypeResolver`, `IXamlResourceResolver`, `IXamlRootInstanceFactory`, `IXamlDispatcher`) — the contract calls the first of those `IXamlSourceProvider`, see `docs/adr/0004-loader-boundaries.md`. Do not create placeholder implementations for out-of-scope features.

Use public Avalonia APIs only. Do not copy, fork, or depend on Avalonia/XamlX internal compiler details without a recorded ADR.

Do not use `XDocument` as the round-trip representation — it cannot preserve trivia.

## Conventions

- Central package management: every version lives in `Directory.Packages.props`, `PackageReference` elements carry no `Version`.
- Nullable enabled, warnings as errors, implicit usings disabled, XML docs required on public APIs.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` is active: new public API must be added to the owning project's `PublicAPI.Unshipped.txt` or the build fails. Run `tools/sync-public-api.py <project>` to derive that file from the build rather than writing it by hand — `dotnet format analyzers --diagnostics RS0016` looks like the official route and stops converging part-way. Review the resulting diff: it is the reviewable summary of what the change added to the public surface.
- The surface in `PublicAPI.Shipped.txt` is a promise. Removing or renaming an entry there is a breaking change and belongs in a diff someone read on purpose; the sync tool refuses to do it and says so. Adding goes to `Unshipped` as usual.
- Public API documentation lives in `docs/api/`. A change to the published surface that leaves those guides describing something else is not finished.
- Prefer immutable public models. Keep reflection behind cached resolver services. No global mutable state, no service locators.
- Add a test with every functional change; add a failing test before fixing a bug.
- Small commits with one architectural purpose each.

## Recorded deviations from README

- **Target framework is `net10.0`**, not the `net8.0` suggested by README — see `docs/adr/0001-target-framework.md`.
- **Avalonia 12.1.1**, which forces the test stack to `xunit.v3` and the headless attribute to `[AvaloniaFact]` — see `docs/adr/0002-avalonia-version.md`.
- **A fourth test project**, `ArxisStudio.Markup.Architecture.Tests`, beyond the three in the contract's layout — see `docs/adr/0003-architecture-tests.md`.
- **`IMarkupSourceProvider` rather than the contract's `IXamlSourceProvider`**, and Avalonia resolving types independently of `IXamlTypeResolver` — see `docs/adr/0004-loader-boundaries.md`.
- **The out-of-scope list governs `src/` rather than the whole repository**, which is what lets the showcase carry a property inspector — see `docs/adr/0006-inspector-in-the-sample.md`.

Two more ADRs record decisions rather than deviations: `0005-resource-includes.md` (includes resolved by projecting the document) and `0007-undo-belongs-to-the-workspace.md` (where undo lives, and which of the two write directions a tool should use).
