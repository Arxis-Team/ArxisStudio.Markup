# 5. Resource includes cannot go through the asset loader

Date: 2026-07-31
Status: Accepted — with work outstanding

## Context

The contract requires that `ResourceInclude` and `StyleInclude` "work through supplied
source/resource providers", so that a document assembled in memory, or an unsaved edit, resolves
like anything else. Milestone 9 delivered everything else in its list and stopped here.

Avalonia resolves an include's `Source` *during* the load, through the asset loader registered in
its service locator, and throws if it cannot find it. `StyleInclude.Loaded` and
`ResourceInclude.Loaded` are read-only. Two approaches were tried or examined.

### Post-load substitution — does not work

Load the include's target through `IXamlResourceResolver` afterwards and swap the include object
for the thing it stands for, in the collection it already sits in.

It cannot run. Avalonia throws during the load, so there is no tree to walk afterwards. The
implementation and its tests were removed rather than left in place half-working.

### Replacing `IAssetLoader` — not reachable

Put a bridging `IAssetLoader` in front of Avalonia's, delegating to the caller's resolver and
falling back for anything it does not know. `AvaloniaLocator.EnterScope()` returns an
`IDisposable`, so the override would have been scoped to one load rather than left on the process
— which is what would have made replacing a global service acceptable.

`AvaloniaLocator` is not public in Avalonia 12.1.1. `Current`, `CurrentMutable` and `EnterScope`
are visible to reflection and invisible to the compiler. Reaching them means reflecting into
Avalonia's internals, which contract rule 6 forbids without an explicit architectural decision,
and which would break on any Avalonia update.

## Decision

Neither approach is taken. The capability is deferred, and the route chosen for it is **text
projection**: resolve includes through the resolver and hand Avalonia a projection of the
document with their content in place, keeping the document itself untouched.

## Consequences

- The document stays the source of truth. Only the text handed to Avalonia is transformed, and
  it is never written anywhere.
- Line numbers in the projection do not match the document, and the object map built in
  milestone 7 is keyed on exactly those line numbers. The projection must therefore carry an
  offset map from projected position back to original position, and `XamlObjectMap` must consult
  it. This is the real work outstanding, and the reason the approach was not attempted in the
  same sitting as the two above.
- Until it is done, includes resolve the way Avalonia resolves them: `avares://` and files its
  own asset loader can find. The resource graph from milestone 5 still analyses the dependencies
  for invalidation; only loading bypasses the resolver.
- If a future Avalonia makes `AvaloniaLocator` public, the bridging asset loader becomes the
  better answer — it needs no offset map at all — and this decision should be revisited.
