# Editing

Changing a document without disturbing anything you did not name.

## What an edit is

An edit is a `TextChange` over the snapshot the document was parsed from. Setting an attribute's
value replaces the text between its quotes and nothing else — not the quote characters, not the
spacing around the equals sign, not the other attributes, not the children. Preserving unrelated
source is not something the editor does afterwards; it is what the changes are.

Two ways in. One edit at a time, on the document:

```csharp
XamlDocument edited = document.SetAttribute(button, XamlQualifiedName.Parse("Width"), "160");
```

Or several at once, through an editor:

```csharp
XamlDocument edited = document.Edit()
    .SetAttribute(button, XamlQualifiedName.Parse("Width"), "160")
    .RemoveAttribute(title, XamlQualifiedName.Parse("Margin"))
    .InsertElement(panel, 2, "<Separator />")
    .Apply();
```

Batching is not a convenience. Each change is computed against *this* document's spans, so applying
several together is the only way for them to be expressed in terms of the same text. Recording an
edit, applying it, and then recording the next one against the old document would cut the second
one in the wrong place — which the editor refuses to do:

```csharp
// Throws: two edits change overlapping regions.
document.Edit().RemoveElement(button).SetAttribute(button, name, "x").Apply();
```

Nodes must belong to the document the editor was opened on. One from a different parse points into
different text, and using it is rejected rather than approximated.

## Attributes

```csharp
editor.SetAttribute(element, XamlQualifiedName.Parse("Width"), "160");
editor.SetAttribute(element, XamlQualifiedName.Parse("Text"), XamlValue.Parse("{Binding Name}"));
editor.SetAttribute(element, XamlQualifiedName.Parse("Text"), XamlLiteralValue.FromPlainText("{}"));
editor.RemoveAttribute(element, XamlQualifiedName.Parse("Width"));
```

An existing attribute keeps its quote character and its position in the tag. A new one is appended
after the last, using the whitespace that already separates the existing ones — so a tag that puts
each attribute on its own line gets the same treatment, indentation included.

The string overload reads text exactly as reading an attribute reads it: `{Binding Name}` sets a
binding, `{}{literal}` sets a literal brace. Pass `XamlLiteralValue` when the text must stay
literal whatever it looks like.

Removing takes the whitespace that separated the attribute from what came before it, so nothing is
left with a double space or a dangling indented blank in the middle of a tag.

## Elements

```csharp
editor.InsertElement(parent, index, "<Button Content=\"Save\" />");
editor.InsertElement(parent, index, someElement);          // its exact text
editor.RemoveElement(element);
editor.ReplaceElement(element, "<ToggleButton Content=\"Save\" />");
editor.MoveElement(element, newParent, index);
editor.WrapElement(element, "<Border Padding=\"8\"></Border>");
editor.UnwrapElement(border);
```

`index` counts **content children only** — property elements are not positions, so index 0 in a
panel that declares `<Panel.Resources>` is before its first control and after the resources. A value
at or beyond the end appends. `element.IndexInContent` is the index an element already sits at, so
"put it back where it was" needs no counting. Inserting copies the indentation of the sibling it
lands next to. Inserting into a self-closing element opens it: `<Grid />`
becomes `<Grid><Button /></Grid>`, with the whitespace before the slash going with the slash. That is
the element's one lossless expansion rather than a choice among several, and an empty container is
written self-closing by every convention there is — so a tool that inserts into one would otherwise
be doing the same tag surgery by hand, against spans, which is what this editor exists to avoid.

`RemoveElement` takes the whole line when the element had that line to itself, so removal does not
leave its indentation behind as a blank.

`ReplaceElement` is **one change over the element's own span** — which is what makes it different
from removing and inserting. The element's position among its siblings, the whitespace on either
side and everything else on its line are not part of the change and cannot be disturbed by it.

`MoveElement` is expressed as a removal and an insertion of the element's exact text, so it arrives
written precisely as it was — attributes, children, comments and all. Moving an element inside
itself is rejected.

`WrapElement` takes markup with somewhere to put content; `<Border />` is rejected. The wrapped
element moves in one level deeper, and the step is measured from the document rather than assumed:
the difference between this element's indentation and its parent's is what the file already uses,
whether that is two spaces, four, or a tab. `UnwrapElement` is the inverse, and wrapping then
unwrapping returns the document character for character.

Property elements — `<Grid.ColumnDefinitions>`, `<Border.Resources>` — are members of their parent
rather than things beside their siblings. They are not unwrapped out into the open, and they take
no part in reordering.

## Duplicating

```csharp
editor.DuplicateElement(element);                              // names removed
editor.DuplicateElement(element, XamlDuplicateNames.Keep);     // names kept
```

The copy goes straight after the original, among the same siblings, written exactly as the original
is written — attributes, children, comments and all.

Names are the reason this takes an option. Avalonia registers an `x:Name` once per scope and refuses
a second, so a copy that keeps them will not load as it stands. `Remove` — the default — strips
`x:Name` and a literal `Name` from the copy and everything inside it, which is what makes the result
loadable. `Keep` is for a caller that will rename them itself before the document is loaded again.

Duplicating the root is rejected: it has no parent to be duplicated within, and a document has one
root. So is duplicating a property element — an element has each of its members once, so there is no
position for a second `<Grid.ColumnDefinitions>` to take.

`x:Key` is copied unchanged, and a resource dictionary refuses a second entry under the same key
just as a name scope refuses a second name. Give the copy a key of its own before the document is
loaded again; which key is a question about your tool's naming.

## Getting the changes out

```csharp
ImmutableArray<TextChange> changes = editor.GetTextChanges();   // ordered, non-overlapping
bool anything = editor.HasChanges;
XamlDocument result = editor.Apply();                            // reparses; the original is untouched
```

`GetTextChanges` is what you hand to a text buffer, an editor control, or a workspace — see
[Workspace and history](workspace.md). `Apply` is the shortcut when you only want the new document.

Documents are immutable. `Apply` returns a new one and leaves the old one exactly as it was, which
is what makes an undo stack a matter of keeping the previous text rather than reversing anything.
