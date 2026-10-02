# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## The contract

`README.md` at the repository root is the **architectural contract**, not a description. It defines three packages, their boundaries, milestones 0 to 18, and an explicit out-of-scope list. Read the relevant section before changing anything, and report any required architectural deviation *before* implementing it.

Development proceeds one milestone at a time. Do not skip ahead; do not start runtime loading before the text model and lossless document model have passing tests.

## Build and test

```bash
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release

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

**`ArxisStudio.Markup.Xaml`** — lossless lexer and parser (tokens *and* trivia), syntax tree, namespaces and directives, markup-extension parser, structured edits, namespace declarations a document lacks, markup that crosses documents (`XamlFragment`), `Preserve`/`Format` serialization, resource/style include discovery, style declarations and their targets. It must not instantiate objects, execute markup extensions or type converters, classify Avalonia properties, or reference Avalonia assemblies.

**`ArxisStudio.Markup.Xaml.Loader`** — `XamlLoadEnvironment`, assembly/type/resource resolvers, `XamlLoadSession`, node↔object mapping with origin tracking, member classification, controlled `SetValue`, `x:Class`, design mode, incremental document updates, what a document is, live documents (a document with its own history kept in step with its session), the type catalog and what a binding reads.

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

Never add to these packages: MSBuild evaluation, `.sln`/`.csproj`/`project.assets.json` reading, NuGet search or restore, C# compilation, Roslyn analysis, IDE integration, a visual designer, selection adorners, property-inspector UI, drag and drop, pointer/keyboard interception, or a sandbox for untrusted XAML. This governs `src/`; a sample may demonstrate what a host builds on the published API — the showcase has a property inspector for exactly that reason, recorded in `docs/adr/0006-inspector-in-the-sample.md`. External environments enter only through the resolver/provider interfaces (`IMarkupSourceProvider`, `IXamlAssemblyResolver`, `IXamlTypeResolver`, `IXamlResourceResolver`, `IXamlRootInstanceFactory`, `IXamlDispatcher`, `IXamlCompilationScope`, `IXamlRootAccess`) — the contract calls the first of those `IXamlSourceProvider`, see `docs/adr/0004-loader-boundaries.md`. Do not create placeholder implementations for out-of-scope features. Showing a loaded root is on the far side of this line too: a `Window` cannot be the content of anything, the stand-in a designer needs for it is an object the document does not describe, and it lives with the editor that shows forms — `UiDesignerFormItem` in ArxisStudio.Surface. A package here did that job for a while and was removed; do not bring it back. See the note that closes `docs/adr/0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md`.

Use public Avalonia APIs only. Do not copy, fork, or depend on Avalonia/XamlX internal compiler details without a recorded ADR.

Do not use `XDocument` as the round-trip representation — it cannot preserve trivia.

## Conventions

- Central package management: every version lives in `Directory.Packages.props`, `PackageReference` elements carry no `Version`.
- Nullable enabled, warnings as errors, implicit usings disabled, XML docs required on public APIs.
- The public surface is not tracked in a file. There is no `PublicAPI.*.txt` and no analyzer enforcing one, so what a change adds to the surface is visible only in the diff of the code itself — read it with that in mind, and say in the commit message what was added.
- Public API documentation lives in `docs/api/`. A change to the public surface that leaves those guides describing something else is not finished.
- Prefer immutable public models. Keep reflection behind cached resolver services. No global mutable state, no service locators.
- Add a test with every functional change; add a failing test before fixing a bug.
- Small commits with one architectural purpose each.

## Recorded deviations from README

- **Target framework is `net10.0`**, not the `net8.0` suggested by README — see `docs/adr/0001-target-framework.md`.
- **Avalonia 12.1.1**, which forces the test stack to `xunit.v3` and the headless attribute to `[AvaloniaFact]` — see `docs/adr/0002-avalonia-version.md`.
- **A fourth test project**, `ArxisStudio.Markup.Architecture.Tests`, beyond the three in the contract's layout — see `docs/adr/0003-architecture-tests.md`.
- **`IMarkupSourceProvider` rather than the contract's `IXamlSourceProvider`**, and Avalonia resolving types independently of `IXamlTypeResolver` — see `docs/adr/0004-loader-boundaries.md`.
- **The out-of-scope list governs `src/` rather than the whole repository**, which is what lets the showcase carry a property inspector — see `docs/adr/0006-inspector-in-the-sample.md`.

Twenty-one more ADRs record decisions rather than deviations: `0005-resource-includes.md` (includes resolved by projecting the document), `0007-undo-belongs-to-the-workspace.md` (where undo lives, and which of the two write directions a tool should use), `0008-an-index-counts-content.md` (an editing index counts content children, and property elements are not positions), `0009-member-resolution-belongs-to-the-environment.md` (member metadata is cached per environment, and conversion can be asked before writing), `0010-a-session-says-how-far-an-update-got.md` (an update reports whether it wrote before failing, there is no generic rollback, and one session mutates at a time — async updates queue, sync edits refuse) and `0011-what-a-review-found-in-the-mutation-boundary.md` (the disposed and state checks belong inside the gate, a post-write failure keeps the recovery document, an invoked setter that throws is never clean, and the queue is FIFO by construction rather than by a semaphore's goodwill), `0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md` (superseded — a `TopLevel` root is made viewable outside the loader, because a surrogate is not a load result; the package that did it here moved to ArxisStudio.Surface as its form container, and what still stands is that the loader never hosts), `0013-a-compilation-scope-comes-with-the-environment.md` (an environment whose assemblies live in replaceable load contexts supplies a scope, and the session enters it around every compilation — the mechanism belongs to the environment's owner, never to the session), `0014-instances-of-compiled-controls-populate-from-live-documents.md` (`XamlLivePopulation` overrides the populate Avalonia compiled into an `x:Class` type with one built from a live document — the sanctioned reach into generated members, with the compiled markup as the reported fallback), `0015-a-session-populates-an-x-class-root-inside-its-constructor.md` (a session lends that same hook its document for the one construction of an `x:Class` root, so the root is populated once and its constructor sees the shown tree; whatever was installed is put back) and `0016-a-document-is-classified-by-its-root-and-by-the-templates-it-sets.md` (style declarations are read from the syntax in `ArxisStudio.Markup.Xaml` and given meaning in the loader; a templated control's look is a template set on a control written outside Avalonia's namespace — a namespace, not an assembly, so it classifies before a build — and a root nobody can resolve is `Unknown`, never guessed from its name), `0017-a-class-the-load-cannot-use-is-left-out-of-the-text.md` (an `x:Class` that does not resolve, or is not what the root says, is withheld from the projection as a handler with nothing to hook up to is, and the root is built as the element it is written as — in every projection of the session, not only the first), `0018-markup-crosses-documents-as-a-fragment.md` (the editor declares what a name needs, on the root and never as the default; markup from another document travels as a `XamlFragment` carrying its declarations, and its prefixes are renamed only where they mean something else, at the places the syntax says name a namespace), `0019-a-rebuilt-part-is-built-without-the-class-and-its-handlers-are-hooked-up-to-the-root.md` (a part, the root's content included, is built as the element it is written as, a top-level copy is closed, and the handlers the class answers are left out of it and hooked up to the session's root by public reflection — a mismatch is a warning, and a changed handler rebuilds its element), `0020-a-rebuilt-part-carries-its-scope.md` (the nearest `x:DataType` and `x:CompileBindings` go onto a part's root, a part that reads a static key from outside itself is rebuilt with the element that answers it — past the outermost include where none declares it — and a rebuild inside another is the outer one's), `0021-values-and-expressions-are-written-where-they-stand.md` (`ClearProperty` and `SetExpression` write in place what a load would evaluate with nothing but the element — never a binding where bindings compile — every in-place write ends the binding it replaces, an `mc:Ignorable` that only gains unused namespaces is no change, and the enum's order is its cost), `0022-a-recorded-position-declares-an-object-only-inside-the-element-the-walk-is-in.md` (Avalonia names every runtime text alike, so a recorded position counts only inside the parent's element, a placed control is its form's element and its own markup's objects are nobody's, and a synchronous edit keeps the map whole), `0023-a-host-lends-the-root-back-for-the-length-of-one-write.md` (an update's writes, map, design values and handlers are one dispatcher turn, and `IXamlRootAccess` lends a borrowed root back around every write), `0024-a-placed-class-is-constructed-by-the-session.md` (a part rooted at a control with compiled markup is constructed by the session, so its own markup populates it and a live registration survives, and `ApplyRebuildAsync` builds elements again for a control whose markup changed), `0025-a-live-document-owns-its-history-and-its-session.md` (`XamlLiveDocument` records every change in the text first and shows it after — in place, by a new session, or `Behind` on the last session that can be believed, never from a text that does not parse; opening is not a step, changed is a difference of text, text from outside is a step that never overwrites unsaved work silently, one operation at a time with events on the owning thread, and detaching lets go of the environment) and `0026-types-and-data-are-answered-by-name.md` (what a tool keeps about types and bindings is names — `XamlTypeCatalog`, `XamlBindableMember` — read the way the resolver reads the namespace mappings, with no static cache; what it asks in passing may answer with a type it must let go; and a binding path is `NotUnderstood`, never `Broken`, beyond what types can say).
