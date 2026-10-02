# 23. A host lends the root back for the length of one write

Date: 2026-10-02
Status: Accepted. Extends [0010](0010-a-session-says-how-far-an-update-got.md) and [0012](0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md)

## Context

A `Window` cannot be the content of anything, so an editor shows a stand-in for it — ArxisStudio.Surface's
form container borrows the window's content, resources and styles while the form is on its canvas. The
session knows nothing of that, and should not: showing a root is outside these packages (ADR 0012).

But the session writes into the root's tree, and reads it to rebuild the map. An update wrote in one
turn of the dispatcher and adopted the document — map, design values — in the next. Between the two the
stand-in held the content again, so the map was built over a window with nothing in it, and every
element below the root came out unpaired. The designer sample worked around it by detecting the empty
map and loading again. And a cancellation arriving between the two turns left objects written and a
document not adopted, which ADR 0010 can only call a broken session.

## Decision

**An update writes, rebuilds the map, applies design values and hooks handlers up in one dispatcher
turn.** Cancellation is observed before the turn and not inside it.

**A host that borrows parts of the root says so with `IXamlRootAccess`**, carried by
`XamlLoadOptions.RootAccess`. The session calls `Lend(root)` on the objects' thread around every write
it makes to the tree — the update's turn, `SetValue`, the document side of `SetXamlValue` — and disposes
the lease when the write is over, in a `finally`, whether the write landed or not. The host gives the
parts back in `Lend` and borrows them again on disposal. Nothing is lent for reading: the map is rebuilt
inside the write's turn, and the readers do not walk the root's children.

## Consequences

- The map built by an update over a borrowed window pairs every element whose object exists, and the
  sample's reload-on-empty-map goes away.
- A cancelled update either wrote nothing or adopted everything.
- The host's stand-in is responsible for being cheap to give back and take again: it happens on every
  write. ArxisStudio.Surface's form container implements it by suspending what it borrowed.
- The interface has one member. A host that needs to know when the root itself is replaced — a new
  session after `RecreateSession` — is told by whatever replaces the session; that is a later decision,
  with the live document that owns sessions.
