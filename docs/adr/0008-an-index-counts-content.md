# 8. An index counts content children, and property elements are not among them

Date: 2026-08-02
Status: Accepted

## Context

`XamlElement.Elements` is every element written inside another one, in document order. That
includes property elements — `<Grid.ColumnDefinitions>`, `<Border.Resources>` — which are members
of their parent rather than things standing beside its children: they produce no object, they
cannot be named, they cannot change places with a control.

Every part of the library that reasons about children therefore filtered them out, and the same
filter was written five times: in `XamlElementIdentity.Pair`, in `XamlLoadSession.Pair`, in
`XamlDocumentEditor.UnwrapElement`, in `XamlDocumentDiff`, and once more in the showcase's tree.
The API guide had to warn about it in a paragraph of its own.

`XamlDocumentEditor.InsertElement(parent, index, …)` did not filter. It counted `Elements`, so
"insert as the first control" of a panel that declares `<Panel.Resources>` put the new element
**before the resources** — and no combination of the published API could express what the caller
meant without recounting the property elements by hand. `MoveElement` had the same flaw.

## Decision

An index in the editing API counts **content children only**, and the distinction is published
rather than re-derived:

- `XamlElement.ContentElements` — the children that produce objects.
- `XamlElement.MemberElements` — the property elements.
- `XamlElement.IndexInContent` — where a child sits among its content siblings.
- `XamlElement.Identity` — `x:Name`, then a literal `Name`; the rule `XamlElementIdentity` was
  keeping to itself.

`InsertElement` and `MoveElement` were changed rather than joined by a second pair of methods that
count differently. Inserting at index 0 into an element that only declares members lands after
them, because that is where its first child would go.

## Consequences

- The old behaviour is gone. It differed only for parents that declare property elements, where it
  was wrong in a way nothing could work around; before 1.0, correcting it beats carrying it.
- A tool no longer writes `!IsPropertyElementSyntax` anywhere. `IsPropertyElementSyntax` remains,
  because a tool inspecting one element still needs to know what it is looking at.
- `XamlElementPath` addresses through members by name (`/1/Resources:0`), so a brush inside
  `<Border.Resources>` is reachable while member elements themselves are not positions.
- The two orders are now separately available, which is what lets a tree show a container's
  controls above the resources it declares without inventing the split itself.
