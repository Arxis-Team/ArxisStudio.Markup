---
name: markup-contract
description: Verify a milestone against the ArxisStudio.Markup architectural contract before calling it done or committing. Use when finishing a milestone, before committing work in this repository, when deciding which of the three packages a new type belongs in, or when a change looks like it might cross a package boundary or touch out-of-scope functionality.
---

# Milestone and boundary verification

`README.md` at the repository root is the architectural contract. This procedure checks work against it before a milestone is declared complete.

## 1. Locate the milestone

Find the current milestone section in `README.md` (`## Implementation milestones`). Read its bullet list and its **Exit criteria**. Those criteria are the definition of done — not your own judgement of "it works".

## 2. Run the mechanical checks

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release
```

Both must be clean. `TreatWarningsAsErrors` is on, so a warning is a failure.

Pay particular attention to `tests/ArxisStudio.Markup.Architecture.Tests`. It is the executable form of the contract's *Explicit non-responsibilities*, *Terminology* and *Out of scope* sections:

- dependency direction holds and is acyclic
- `ArxisStudio.Markup` and `ArxisStudio.Markup.Xaml` reference nothing named `Avalonia*`, checked both in the `.csproj` graph and in compiled assembly metadata
- no public type name contains `Axaml`/`AXaml`/`AXAML`
- no package reference to MSBuild, Roslyn or NuGet infrastructure
- all three packages ship XML documentation

If one of these fails, the fix is to move the code to the correct package. Never relax the test.

## 3. Check placement of new types

For each new public type, confirm it sits in the right package:

| The type… | belongs in |
| --- | --- |
| knows nothing about markup formats | `ArxisStudio.Markup` |
| understands XAML syntax, trivia, or document structure | `ArxisStudio.Markup.Xaml` |
| touches Avalonia types, creates objects, or resolves CLR metadata | `ArxisStudio.Markup.Xaml.Loader` |

Two recurring traps:

- Classifying a member as Styled/Direct/Attached/CLR/event is a **Loader** responsibility. The syntax package may only recognise the `Owner.Member` *shape*.
- Discovering `ResourceInclude`/`StyleInclude` dependencies is a **syntax** responsibility. Actually loading and applying those resources is **Loader**.

## 4. Check nothing out-of-scope crept in

Scan the diff for MSBuild, `.csproj`/`.sln` parsing, NuGet, Roslyn, C# compilation, designer UI, adorners, input interception, or sandboxing. The contract's `## Out of scope` section is exhaustive and forbids placeholder implementations too.

Also verify the three invariants held:

1. No code path regenerates XAML from a runtime object tree.
2. No code path replaces a binding or resource expression with its effective value.
3. Unknown content still survives a round trip.

## 5. Public API surface

`PublicApiAnalyzers` fails the build on undeclared public API. Every addition belongs in the owning project's `PublicAPI.Unshipped.txt`. Review that file's diff deliberately — it is the reviewable summary of what this milestone added to the public surface.

## 6. Deviations

If the work required departing from the contract, it needs an ADR in `docs/adr/` and a line in the *Recorded deviations* section of `CLAUDE.md`. Contract rule 11: report a required architectural deviation before implementing it, not after.

## 7. Commit

One architectural purpose per commit. If the milestone produced several independent concerns, split them.
