# 29. A document writes what the XAML compiler sets

Date: 2026-10-11
Status: Accepted

## Context

`XamlMemberResolver` resolves a simply named member through Avalonia's property registry and called
it writable whenever the Avalonia property was not read-only. The object agrees: `SetValue` takes any
styled property. A document does not. Avalonia's XAML compiler — the runtime one a session loads with,
and the one in the project's build — sets a simply named member through its public CLR setter:

- a styled property whose wrapper only reads, as `StyledElement.ActualThemeVariant`'s does, fails with
  "Unable to find suitable setter or adder for property ActualThemeVariant";
- a styled property registered with no wrapper on the type, as `InputElement`'s `IsHoldWithMouseEnabled`,
  fails with "Unable to resolve suitable regular or attached property".

A property inspector built on the descriptor wrote both into a form. The session's in-place write
succeeded on the object, the document took the attribute, and the project's next build failed on it.

## Decision

**A simply named Avalonia property is writable only through a public CLR setter on the type or a base.**
Without one, its descriptor says `CanWrite` false and `IsReadOnly` true, and an edit through the session
is refused with `ReadOnlyMember` before either side is touched. Reading it is unchanged: the member is
still resolved and listed, and a tool can show its value.

**An attached property is written by its owner's name, unless the type wraps it.** The registry answers
some attached properties under a type's simple names — `InputElement` registers `IsHoldWithMouseEnabled`
as an attached property of its own and wraps it in nothing — and the compiler resolves
`IsHoldWithMouseEnabled` on a `TextBlock` to nothing and `InputElement.IsHoldWithMouseEnabled` to the
property. So such a property is listed and described under `Owner.Member`, written that way by an edit
through the session, and read-only under its simple name. One the type does wrap with a public setter —
`TextBlock.FontSize`, `KeyboardNavigation.IsTabStop` on a control — stays the type's own member, written
simply. This part was added the same day, after a designer's test showed the first decision had left it.

## Consequences

- A tool offering the members a document can write offers what the document can load and the project
  can build.
- A document that already carries such an attribute is not rewritten: its load fails as Avalonia's
  does, and says so.
