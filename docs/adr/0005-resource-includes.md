# 5. Resource includes cannot go through the asset loader

Date: 2026-07-31
Status: Accepted

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

Neither approach is taken. The route is **text projection**: includes are resolved through the
resolver and Avalonia is handed a projection of the document with their content in place, while
the document itself is untouched.

`TextProjection` in `ArxisStudio.Markup` is the general form of this — a text assembled from one
document plus runs spliced in from others, carrying the segments that map any position in the
result back to the file and offset it really came from. It knows nothing about XAML.
`XamlIncludeProjector` in the loader is what drives it: it discovers includes with the syntax
package's analyser, resolves each through `IXamlResourceResolver`, and recurses, so a chain of
includes across several files becomes one text with one flat map.

## Consequences

- The document stays the source of truth. Only the text handed to Avalonia is transformed, and
  it is never written anywhere. `XamlLoadSession.Document` still round-trips byte for byte.
- `XamlLoadSession.Projection` exposes the projection, and `XamlObjectMap` consults it. Line
  numbers in the projection do not match the document, and the object map from milestone 7 is
  keyed on exactly those line numbers; without the map back, an object declared in an included
  dictionary would be attributed to whichever element of the host document happens to sit at the
  same line. Runtime diagnostics are mapped the same way, so a fault in an included file is
  reported against that file's URI.
- `XamlObjectMap.GetSourceUri` reports the file an object's markup is in, which for anything an
  include produced is not the document being edited.
- Avalonia's XAML parser accepts `xmlns` declarations only on the root element. A spliced-in
  fragment therefore cannot keep its own: they are stripped from it and added to the root of the
  projected text. This is why `TextProjection` allows a synthesized run at all.
- Moving a declaration is only meaning-preserving if it says which assembly it means.
  `using:Some.Namespace` and an unqualified `clr-namespace:Some.Namespace` mean "the assembly of
  the file this is written in", so hoisting them verbatim would repoint them at the host's
  assembly. They are rewritten to `clr-namespace:…;assembly=…` naming the assembly of the
  `avares://` URI the fragment came from, which is the only place that assembly is recorded.
- An include whose target no resolver knows is left exactly as written, and Avalonia's own asset
  loader still gets its chance at it — `avares://` URIs from assemblies this library was never
  handed keep working. The same is true of a cycle, a malformed included file, and the one case
  the projection cannot express: an included file that binds a prefix the host already binds to
  something else. Each is reported (`AXM2007`–`AXM2011`) rather than guessed at.
- A relative `Source` on an include that is being left as written is rewritten in the projection
  to the URI it already resolved to. Relative means "beside the file it is written in", and the
  file it is written in is no longer where Avalonia thinks it is once the fragment has moved.
  Relative URIs in any other attribute cannot be found without CLR metadata and are not
  rebased — a limitation, not a decision.
- If a future Avalonia makes `AvaloniaLocator` public, the bridging asset loader becomes a
  simpler answer for the loading half — it needs no namespace hoisting — though the projection's
  map would still be the thing that keeps included markup attributable. This decision should be
  revisited then.
