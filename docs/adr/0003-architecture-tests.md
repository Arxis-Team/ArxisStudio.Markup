# 3. A dedicated architecture test project

Date: 2026-07-31
Status: Accepted

## Context

Milestone 0's exit criteria include "package dependency direction is enforced". A sentence in a document is not enforcement — it degrades the moment someone adds a convenient `using`.

The contract's repository layout lists exactly three test projects, one per package. Placing cross-cutting guard tests in one of them creates a problem: to inspect all three compiled assemblies, the test project must reference all three. Putting that in `ArxisStudio.Markup.Tests` would make the *base* package's test suite depend on the *top* of the stack, and drag Avalonia into the output of the one project that most needs to stay clean of it.

## Decision

Add a fourth test project, `tests/ArxisStudio.Markup.Architecture.Tests`, holding only cross-cutting guard tests. It references all three packages deliberately.

This is an additive deviation from the contract's layout. It introduces no new package, no new public API, and nothing from the out-of-scope list.

The guards check the contract from two independent directions:

- **Declarative** — the `ProjectReference`/`PackageReference` graph read from the `.csproj` files. This is the authoritative statement of intent and works even when a package has no code yet, which matters at milestone 0 where the compiler would prune every unused reference.
- **Compiled** — `Assembly.GetReferencedAssemblies()` on the built assemblies. This catches Avalonia reaching the syntax layer transitively, which the project files cannot show.

Covered: dependency direction, acyclicity, Avalonia absence from the two lower packages (both ways), `Axaml` naming in public types, out-of-scope package references (MSBuild, Roslyn, NuGet), and presence of XML documentation.

## Consequences

- The exit criterion is checked by `dotnet test`, in CI, on every change — not by review attention.
- Both guards were mutation-tested when written: adding an Avalonia `PackageReference` to `ArxisStudio.Markup.Xaml` fails the declarative test, and additionally using an Avalonia type fails the compiled-metadata test. A guard that has never been observed to fail is not a guard.
- When a guard fails, the correct response is to move the code into the right package. Relaxing the test defeats its only purpose.
- The declarative guard reads project files at test time and therefore needs the repository root, supplied as `AssemblyMetadata` from the test project file. Moving the project without updating that metadata will fail loudly rather than silently skip.
