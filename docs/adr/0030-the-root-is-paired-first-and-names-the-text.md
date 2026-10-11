# 30. The root is paired first, and names the text

Date: 2026-10-11
Status: Accepted

## Context

`XamlObjectMap` pairs objects with elements by the positions Avalonia records on what it builds, read
back into the document. It tells this document's markup from everybody else's by the name Avalonia gave
the text — `file://runtimexaml0/` for every runtime load — which it learnt from the root object. The root
of an `x:Class` document records no position: the instance is constructed before the markup is loaded
into it. `PairTheRoot` paired it afterwards, and only when the walk had left both sides free.

With no name learnt, every recorded position was read as one in this document. A designer dresses a
form-window on its canvas with a window chrome whose template is compiled markup; built in Debug, it
records positions in its own file. The walk reached the root's template before anything else could pair
the root element, read the chrome's position into the form, and paired the template with the root
element. `PairTheRoot` then found the element taken and left it. The designer's inspector described the
form as that template — `ControlTemplate`, with `Content` and `TargetType` — on a project whose window had
just become constructible at all.

## Decision

**The root is paired before the walk.** The object a document produced is its root element's by
definition, not by evidence: the walk has only recorded positions, and a position recorded against
somebody else's text is exactly what took the element. Pairing first also puts everything below the
root under the rule of ADR 0022 — a position must fall inside the parent's element — so nothing can take
the root or land outside it. What an update carried across is left alone: it is the update's own account
of the same root.

**The text's name is learnt from the nearest declared descendant when the root has none.** Breadth-first
through the logical tree, past anything a template produced: what the root holds was loaded from the
document, and the shallowest of it that records a position names the text — deeper may be another
control's own markup. Positions from any other text are then refused, as they are under a root that
records its own. A root holding nothing that recorded one leaves the name unknown, and the first pairing
is what keeps it its own.

## Consequences

- A form whose class is constructed is described by its own root wherever it is shown and however the
  host dresses it.
- An `x:Class` document whose root holds nothing still accepts positions from any text below the root —
  under the containment of ADR 0022, which is what the root's first pairing gives it.
- The difference shows only where foreign markup records positions — compiled in Debug — which is where
  a designer runs while it is being worked on, and where the tests that hold it run.
