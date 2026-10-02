# 17. A class the load cannot use is left out of the text

Date: 2026-10-02
Status: Accepted

## Context

`XamlRootClass` has always meant to carry on without a class it could not use. Its remarks said so —
a document naming a class that has not been compiled yet is ordinary in an editor, and refusing to
show anything would be worse than showing the markup with a diagnostic beside it — and its resolver
reported the class and returned no type. What it could not stop was the directive itself: `x:Class`
stayed in the text the session handed Avalonia, and Avalonia's runtime loader resolves it on its own.
It does not find the class either, and fails the whole document: `AXM3020`, then `AXM3002` "Unable to
resolve type", then `AXM3003`, and no session. A class that resolved but was not what the root said
did worse: Avalonia built the class instead of the element the root is written as.

For a designer this is the first file it opens. A form created from a template exists in source and
not in any built assembly until the project is built; a renamed class and a project that does not
compile are the same state. Showing nothing in all three is the trade the remarks already said was
wrong.

The mechanism to withhold something from Avalonia already existed. `XamlAttributeChecks` decides what
the projection leaves out so that the load survives — a handler the class does not declare, and every
handler when there is no class, reported with `AXM3005` and removed from the projected text while the
document keeps it. The README's events section says the library does not suppress events; this has
been read, since milestone 8, as not suppressing a hookup that could happen. A handler with no method
to name can be hooked to nothing.

A second fact surfaced while testing the first. The update path projected the document without the
checks at all — every `ProjectAsync` call in `XamlLoadSession.Updates` passed an empty list — so a
session that loaded only because something was withheld rebuilt its parts from text that put it
back. The root's content, rebuilt from a projection of the whole document, failed on the directive;
a panel holding a handler failed on the handler; and a source update, which compares its projection
with the load's to decide whether anything changed, saw the withheld text as a change. That last one
was there before this decision, for any document with a handler nobody had written.

## Decision

**A class the load cannot use is withheld from the projection, as a handler with nothing to hook up to
is.** When `XamlRootClass` resolves no usable type — none found, or one that does not derive from the
root element's type — `XamlAttributeChecks` adds the `x:Class` directive to what the projection leaves
out. The root is built as the element it is written as, in either load mode: the remarks were never
about design mode, and missing handlers have always been withheld in both.

**What the load withheld, every projection of the session withholds.** The session keeps the class it
populated (`_rootClass`, null when there was none to use) and asks the same checks about every version
of the document it projects — the update's own projection, every fragment it rebuilds, and the
reprojection a source update compares. The answer is worked out once per update and shared by its
fragments.

**The two reasons are reported at different severities.** `UnresolvedRootType` becomes a warning: a
class nobody has built is the environment lagging behind the document, and the load loses nothing the
markup can show. `IncompatibleRootType` stays an error: that is the document contradicting itself,
though its root is still built.

## Consequences

- A form the project has not built is shown, updatable, with its names resolving and its handlers
  reported. `GetRoot<TheClass>()` fails on it, because it is not one; a host that wants the class waits
  for a build, which is a new environment and a new session.
- A source update that changed nothing reads as nothing again for a document whose load withheld
  something, which was a latent fault in its own right.
- An update now reports what the checks notice about the document it is offered — a handler still
  missing, an extension nothing declares — because that is as true of it as it was of the one loaded.
- A rebuilt part that names a handler its class *does* have still cannot be rebuilt in place: a
  fragment is built without the root instance, and Avalonia refuses a handler it cannot hook to
  anything. That is unchanged by this decision and recorded in `docs/limitations.md`; lifting it means
  hooking the handlers of a rebuilt part to the root by hand.
- `XamlLivePopulation` passes the type it was registered for, never null, so the directive is never
  withheld from a live population.
