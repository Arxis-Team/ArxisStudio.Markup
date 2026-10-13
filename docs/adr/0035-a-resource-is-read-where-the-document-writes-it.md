# 35. A resource is read where the document writes it

Date: 2026-10-13
Status: Accepted

## Context

A tool that offers a resource for a property — a picker, a check that a key exists, a jump to its definition —
needs to know which keys a document writes, where, for which theme, and which of them a given element can reach.
The syntax package found the includes a document declares and nothing about the keys: a host had to walk the tree
and guess what makes an element an entry.

What an element reaches was checked against Avalonia 12.1.1 with its compiled markup rather than assumed. A
`StaticResource` finds the element's own resources, an ancestor's written after the use, a key written later in
the same dictionary, and the resources of a style an ancestor writes; it does not find a sibling's. The order of
the text is not the rule. A key a theme dictionary alone holds is found only where a theme is already known when
the element is built — inside a `Window` or a `ThemeVariantScope` — and is fixed at that theme from then on; a
`DynamicResource` follows the theme. A key that is nowhere gives the property nothing: no exception, and no
error when the project builds.

## Decision

**`XamlResourceDeclarations.Discover` reads a document's keyed entries from the syntax**, in source order, the way
the includes are read: by what elements are called, with no type resolved. An entry is an element with an `x:Key`
inside a dictionary — a `Resources` property element, an element called `ResourceDictionary` there or at the root
of a dictionary document, one merged into another, or a theme dictionary. Each `XamlResourceDeclaration` names the
key, the element that is the value, the element whose resources it belongs to — the control, style or
application writing `Resources`, or the root of a dictionary document — and the key of the theme dictionary it is
in. The theme dictionary's own key names its variant and is not an entry; a key anywhere else is not one either.

**`XamlResourceDeclarations.InScope` answers what an element reaches**: its own entries, then each ancestor's,
nearest first, each with the resources of the styles it writes — whatever the order of the text. An element in a
property element belongs to the element that writes it.

## Consequences

- A host lists the keys a property can use, with the element to jump to, from the document alone, and checks a
  key by name before anything is built.
- A `ResourceDictionary` subclass written under its own name is not recognised: recognising one would take a
  resolved type, which this package never has.
- Entries an include brings in are in the included document; `XamlResourceGraph` reaches it, and a host reads
  its entries the same way.
- Whether a `StaticResource` reads a theme dictionary's key depends on a theme being known while the element is
  built — the types of its ancestors — and is the loader's or the host's to decide. `ThemeVariant` is what the
  text says it needs to.
