# 1. Target `net10.0` instead of `net8.0`

Date: 2026-07-31
Status: Accepted

## Context

README.md, the architectural contract, states: "The initial target framework should be `net8.0` unless existing repository constraints require another target."

`net8.0` is the conservative choice: it is LTS and gives the widest consumer reach for a library. The contract's wording, however, leaves the decision open to repository constraints.

The repository owner chose `net10.0` for the initial implementation. The relevant facts:

- SDKs 8.0.406, 9.0.203 and 10.0.101 are installed on the development machine.
- Avalonia 12.1.1 ships `lib/net8.0` and `lib/net10.0`, so the chosen Avalonia line supports either target (see ADR 0002).
- No dependency in the stack forces `net8.0`.

## Decision

Target `net10.0` for all projects, set once in the root `Directory.Build.props`.

This is a recorded deviation from the contract, made under contract rule 11 ("Report any required architectural deviation before implementing it").

## Consequences

- Consumers of the preview packages must be on .NET 10. This narrows reach relative to `net8.0`, which is the accepted cost.
- The newest C# language and BCL surface is available without conditional compilation. This matters for the text model, where `Span<T>`, `SearchValues` and modern collection expressions are directly useful in the lexer and line-mapping work of milestones 1 and 3.
- Retargeting later is cheap while the code base is small: `TargetFramework` lives in exactly one file. If broader reach becomes a requirement, the natural move is to multi-target `net8.0;net10.0` rather than to downgrade.
- Anything that would only compile on `net10.0` should be kept out of the public API shape where a reasonable `net8.0`-compatible alternative exists, so that a future multi-target does not become a breaking change.
