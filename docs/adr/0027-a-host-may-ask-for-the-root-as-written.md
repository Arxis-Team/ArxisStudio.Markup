# 27. A host may ask for the root as written

Date: 2026-10-03
Status: Accepted. Extends [0017](0017-a-class-the-load-cannot-use-is-left-out-of-the-text.md)

## Context

A designer shows a program's forms inside the designer's own process, and the forms have to look as
they do in the program. What they look like is decided outside them, in the program's `App.axaml`: the
theme it sets in `Application.Styles` and the resources it declares in `Application.Resources`. A
designer whose own application carries a different theme — or, as in ArxisStudio, no general-purpose
theme at all — shows a button with no template unless it takes those styles from the program.

The obvious way to read them is to load `App.axaml` like any other document. A session constructs the
class its `x:Class` names and populates the instance (ADR 0015). For an application that class is the
program's `App`, whose constructor and whatever it reaches are the program's startup code — run inside
the designer, for nothing the designer needs: the styles and resources are in the document, not in the
class.

ADR 0017 already has a road that builds a root without its class. A class the load cannot use is kept
out of the text Avalonia is given, by every projection the session makes, and the root is built as the
element it is written as. That road was taken only when the class could not be used.

## Decision

**What a session does with the class is the host's to say.** `XamlLoadOptions.ClassUse` is
`XamlClassUse.Construct` by default — the class is constructed and populated, as before — or
`XamlClassUse.AsWritten`, which takes the ADR 0017 road on request: the class is not resolved, the
directive is withheld from every projection of the session, and the root is built as the element it is
written as. An `App.axaml` loaded this way is a plain `Application` holding the document's styles and
resources, and `App`'s constructor never runs.

**Nothing is said about a class the host left out.** `UnresolvedRootType` and `IncompatibleRootType`
report something wrong with a document or with its environment; a class nobody asked for is neither,
and reporting it would teach a host to filter diagnostics it should be reading. A handler the document
names has nothing to be hooked up to, as for a document with no class, and is reported and left out —
with a message that says the root was built as written, rather than that the document has no class.

**The result is taken over, not shared.** A style and a resource dictionary have one owner in Avalonia
— adding the application's own to a form's `Styles` throws "The Style already has a parent" — so a host
lending them to a form moves them out of the loaded `Application` into the form, the way a designer's
form item takes a window's resources, and a host showing several forms loads the document once for
each. `ClassUseTests` records both the throw and the road that works.

## Consequences

- A designer can show a program's forms in the program's theme without starting the program inside the
  designer. ArxisStudio's design host reads each form's application layer this way.
- A resource the document declares but nothing asks for is never built: Avalonia defers resource
  content until it is looked up. An update that replaces such a resource finds no object for it and is
  refused, as it would be under any root. A host following edits to an application's document loads it
  again rather than updating the session; the styles and resources are cheap to read and rarely edited.
- The option adds no state: a session loaded as written is updated, rebuilt and disposed exactly like
  one whose class could not be used.
