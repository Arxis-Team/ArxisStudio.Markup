# 19. A rebuilt part is built without the class, and its handlers are hooked up to the root

Date: 2026-10-02
Status: Accepted. Extends [0015](0015-a-session-populates-an-x-class-root-inside-its-constructor.md) and [0017](0017-a-class-the-load-cannot-use-is-left-out-of-the-text.md)

## Context

The update path was written around documents whose class the load could not use, and no test of it
loaded one whose `x:Class` resolved. A designer working beside an IDE shows nothing else.

Rebuilding the root's content builds a copy of the root from a projection of the whole document and
moves the copy's content onto the root. The projection carried `x:Class`, so Avalonia constructed the
class for the copy: the author's constructor ran a second time — a constructor that subscribes to
something or opens a file does that twice — and for a window the copy was a second window with a
platform window of its own. Nobody closed it, and the platform held it, and through it every type it
was built from: the generation a designer wants to unload after a build.

A part below the root is built on its own, from a projection of just that element, through
`AvaloniaRuntimeXamlLoader` with no root instance. A handler in it names a method of the class, and
Avalonia hooks a handler up to the instance it populates — with none, it refuses the handler and the
whole part. So a panel holding `Click="SaveClicked"`, which the class declares, could not be rebuilt
at all, and a child added to it cost a new session.

## Decision

**A part is built as the element it is written as, never as the class.** The projection of the root's
part leaves out `x:Class`, `x:ClassModifier` and `x:Subclass`, which mean something only on a
document's root. The copy is then the element type, and its content moves onto the root, which is the
class: the move accepts a copy whose type the root derives from. A copy that is a top level is closed
once its content has moved. One whose author refuses to close it is reported with `AXM3046`, because
its platform window lives as long as the process.

**A part's handlers are left out of it and hooked up afterwards, to the session's root.** The attribute
checks already know which handlers the class answers; every one inside a rebuilt part is withheld from
its projection, and once the part's objects exist and the map knows them, each is hooked up through
public reflection: the member resolver names the event, its `EventInfo` accessor adds a delegate, and
the delegate is made with `Delegate.CreateDelegate` from the first method of the attribute's name that
fits — the delegate's own rule, which is Avalonia's too. A method that cannot take the event's
arguments is reported with `AXM3045`, a warning, and the update stands without it: a handler the author
is in the middle of writing is the ordinary state of a file in an editor, and refusing the update over
it would refuse every structural edit near it.

**An element whose object stayed keeps the handlers it had.** The root, whose content alone was
rebuilt, and any element rebuilt the same way, were hooked up when they were built; hooking them up
again would run each handler twice per event.

**The class's assembly is the document's, unless the caller names another.** A runtime load compiles
the text into an assembly of its own, which reaches a non-public member of another assembly only when
the load names that assembly as the document's local one — and a handler in code-behind is private as
a rule. A session that was not given `XamlLoadOptions.LocalAssembly` names the assembly of the class it
populates, for the load and for every part it rebuilds; without it, a form whose class declares a
private handler did not load at all. Avalonia remembers the assemblies it has been told to reach for
the life of the process, which is why the test of this keeps its fixture in an assembly no other test
names.

**A handler written, renamed or taken out is its element built again.** An event attribute is not a
value to set — the update path used to try, and refused it as a member it cannot write — and a
delegate hooked up by a load cannot be found again to be taken off. So a changed handler rebuilds its
element, which is then hooked up as above; at the root, whose handlers its load hooked up, it is a new
session.

## Consequences

- A structural change to the root's content constructs the class no second time and leaves no window
  open. A structural change inside a panel holding a handler applies in place, and the rebuilt
  control's handler runs on the root instance.
- The fixtures in `ArxisStudio.Markup.Xaml.Loader.TestControls` now include a view, a window and a
  window that refuses to close, each counting its constructions, so a regression of either half is a
  failing count rather than a leak found in a designer.
- An event written as attached on another element — `Button.Click` on a panel — is a routed event the
  panel has no accessor for. It is reported with `AXM3045` and not hooked up; hooking it up would take
  the routed event's field, found by owner type and name, and has not been needed.
- A handler added in the IDE to a control on the form shows up as a rebuild of that control. On the
  root it costs a new session, which is the one place a handler cannot be followed in place.
