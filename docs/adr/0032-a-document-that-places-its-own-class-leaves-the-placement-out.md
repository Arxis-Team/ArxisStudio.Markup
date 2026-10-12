# 32. A document that places its own class leaves the placement out

Date: 2026-10-12
Status: Accepted. Amends [0014](0014-instances-of-compiled-controls-populate-from-live-documents.md) and
[0015](0015-a-session-populates-an-x-class-root-inside-its-constructor.md)

## Context

A form can place its own class inside itself. The shape it takes in practice is a data context made of
the form:

```xml
<UserControl x:Class="App.Views.MainView" xmlns:views="using:App.Views" …>
    <UserControl.DataContext>
        <views:MainView />
    </UserControl.DataContext>
</UserControl>
```

Constructing `MainView` populates it, and populating it constructs `MainView` again — for as long as
there is stack. Avalonia's XAML compiler builds such a project without a word; the application stops
with a stack overflow on the first construction. ADR 0014 assumed the opposite — that only live documents
can state a cycle, "a project whose compiled markup contained one would never have built" — and built the
cycle's way out on that assumption: the inner instance of a cycle was populated from the compiled markup.

That way out is a way in. A host that registers every document of a project for live population — a
designer does — met the form's own copy inside the form, found the cycle, and handed the copy to the
compiled markup. The compiled markup, built from the same text, constructs the class again, and runs with
the override field cleared, because that is how the trampoline is told to run it (`XamlPopulateHook`): the
next copy reads no hook, runs the compiled markup in turn, and so on. Nothing on that path can stop it, and
a stack overflow is not an exception anybody can catch. The process went with it — the designer, not the
form.

Three more roads lead to the same place. A copy constructed inside the root's own population with no
registration for the type went from the root's loan (ADR 0015) straight to the compiled markup. A copy
left empty can throw from its constructor — a form's code reads the controls its markup names, and an
empty copy has none — and the document around it then fails and falls back to its own compiled markup. And
a type whose live document fails, after a copy of it was constructed inside it, falls back the same way.

## Decision

**The element goes, and says so.** The attribute checks that decide what a projection leaves out
(`XamlAttributeChecks`) also leave out an element of the document's own class, anywhere below the root:
the class the session populates or the registration stands in for, and — where there is no class to use —
the class the document's `x:Class` names. Where the element is all its property element holds, the
property element goes with it, so that what is left still says a member and a value or neither. Nothing
inside it is checked or loaded. `AXM3054 SelfPlacement` is an error at the element's name: the document
as written does not run.

**A cycle bottoms out in an empty instance.** A type constructed again while it is being populated on the
same thread is left without content and said as `AXM3052` — never handed to the compiled markup. The
bookkeeping lives in `XamlPopulateHook`, shared by registrations and by the root's loan, and the root's
own population counts as one: a copy of the root that reaches the loan with no registration to ask is a
cycle like any other.

**A type found placing itself keeps away from its compiled markup.** When its live document then fails,
the instance keeps what the document built before it failed, and `AXM3051` says why the compiled markup
was not used.

## Consequences

- A form that makes a data context of itself loads, without the data context, with an error at the
  element. A designer shows the form; the error says what the application would do.
- A copy of a form inside itself is no longer drawn from a registration (ADR 0015 said it was): it is left
  out of the text, or, reached through another document, empty.
- Two live documents placing each other leave the innermost instance empty, where it used to show the
  compiled markup.
- What remains, said rather than solved:
  - compiled markup that places itself while the live document no longer does — the self-placement was
    built, removed in the text, and not built again — run as the fallback of a live document that fails;
  - a constructor whose own code constructs its class (`DataContext = new MainView()`), which recurses in
    the code rather than in the markup. Loading a document runs constructors, and there is no sandbox
    (`docs/limitations.md`).
- The trampoline is still the only way the compiled markup is run, and run with the field cleared. Calling
  the generated populate directly, with a guard left in the field, would close the first of the two holes
  and reach further into generated members than ADR 0014 sanctions; it is not done.
