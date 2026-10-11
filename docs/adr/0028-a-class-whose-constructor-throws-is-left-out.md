# 28. A class whose constructor throws is left out

Date: 2026-10-11
Status: Accepted. Extends [0017](0017-a-class-the-load-cannot-use-is-left-out-of-the-text.md)

## Context

A session constructs the class a document's `x:Class` names and populates the instance inside its
constructor (ADR 0015). The constructor is the project's code, and it can throw for reasons that have
nothing to do with the document.

One of them came from a designer. A project's XAML failed to compile — two attributes Avalonia's XAML
compiler rejects — and the design build failed. The next design build succeeded: MSBuild found the
intermediate assembly newer than the sources, skipped the C# compiler, and with it the XAML compiler,
which only runs after the C# compiler does. The assembly the designer then loaded called Avalonia's
`AvaloniaXamlLoader.Load(this)` from every generated `InitializeComponent`, which throws "No precompiled
XAML found" — a stub the XAML compiler replaces and had not. Every form of the project failed to load.

It failed twice over, and said neither clearly. `XamlRootClass` reported the reflection wrapper's
message, "Exception has been thrown by the target of an invocation", which names nothing. Then the
load carried on with the directive still in the text, and Avalonia's runtime loader constructed the
class itself, threw the same way and failed the document: `AXM3022`, `AXM3002`, `AXM3003`, no session.

ADR 0017 already has the road for a class the load cannot use: withheld from every projection of the
session, with its handlers, and the root built as the element it is written as. A designer showing a
form whose code cannot run is in the position a WPF designer always is — it never constructs the
`x:Class` at all, only the element the root is written as.

## Decision

**A class whose constructor throws is left out the way of a class nobody has built.** When the
constructor throws before the document was loaded into the instance, or after a load that succeeded,
the session withholds the directive and every handler from this and every later projection and builds
the root as written. `RootInstanceCreationFailure` (AXM3022) becomes a warning that says so: the load
lost nothing the markup can show. A handler the class would have answered has nothing to be hooked up
to and is reported once, with the message for a root built as written.

**Where the document itself failed inside the constructor, nothing changes.** Built without the class
it would fail the same way; there is no root, and AXM3022 stays an error beside the load's own
diagnostics.

**The constructor's own exception is reported, not the wrapper.** `TargetInvocationException` is
unwrapped before its message is used.

**The second projection is made off the owning thread,** for the reason the class is resolved off it: a
host whose resolver marshals to that thread would otherwise wait on itself.

## Consequences

- A project whose code cannot run in the designer — an unfinished build, a constructor that reaches for
  a service the designer does not have — still shows its forms, with the reason beside them.
  `GetRoot<TheClass>()` fails on such a root, as on any root built as written.
- The session's later projections tell a withheld handler that the root is built as written: the
  session keeps how it used the class, not only the class.
- A host that wants the class's code to run waits for a constructor that does not throw, which is a
  new environment and a new session.
