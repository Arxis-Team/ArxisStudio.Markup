# 18. Markup crosses documents as a fragment, and its prefixes are renamed only when they must be

Date: 2026-10-02
Status: Accepted

## Context

An audit of these packages as the base of a form designer found the editor complete for edits within
one document and blind across two. `SetAttribute` writes a name "as it should be written, prefix
included" and never asks whether the prefix is declared; `InsertElement(parent, index, XamlElement)`
inserts `element.GetText()`, so the declarations its names depend on — on an ancestor, almost always
the root — stay behind. The designer demo in ArxisStudio.Surface showed what every host does instead:
a hand-written `Declare` for `xmlns:d`, `xmlns:mc` and `mc:Ignorable`, a clipboard of raw text, and a
`WithoutNames` that strips `x:Name` with an editor it opens on the pasted text.

Three facts about Avalonia shape any answer, and each was measured rather than assumed:

- Avalonia 12.1.1 accepts `xmlns` on the root element only. A declaration anywhere else is
  `XamlParseException: xmlns declarations are only allowed on the root element`, in both load modes.
  A fragment cannot carry its declarations inline once it is inside another document.
- Two prefixes bound to one namespace load, and mean the same.
- A design-time attribute in a namespace that is not listed in `mc:Ignorable` is a build error for the
  XAML compiler, whatever this library's loader makes of it.

## Decision

**Declaring is the editor's.** `Qualify` and `QualifyAttribute` return the name to write for a
namespace at a place — under the prefix the document gives it there, unprefixed for an element in the
default namespace, and otherwise declared on the root. A new declaration goes after the root's
existing ones and copies their layout; its prefix is the one asked for or one made from the namespace,
numbered when the document declares or writes it already — written as well as declared, because a
document using a prefix it never declared is broken in a way a declaration would quietly change the
meaning of. Nothing is ever declared as the default namespace. The design namespace is never declared
without being made ignorable, and `EnsureIgnorable` lists any other. All of it is recorded as
ordinary changes — one insertion for every declaration the editor adds, rewritten as more arrive, so
they appear in the order asked for and refuse together with the rest of the edit.

**A fragment is a document in its own right.** `XamlFragment.From` lifts an element with the in-scope
declarations its text mentions, written onto its own start tag, the ignorable ones listed, and the
indentation it sat at taken off. Mentions are judged from the text, generously, because a declaration
too many costs an attribute and one too few costs markup that does not load. `ToXamlText` and `Parse`
make it a clipboard format.

**Prefixes are reconciled in the least intrusive way that is right.** For each namespace the inserted
text still uses: nothing when the receiving document binds the same prefix to it where the fragment
lands; a declaration on the root under the fragment's own prefix when nothing in the document uses that
prefix, even where the namespace is already there under another; and only when the prefix means
something else here, a rename — to the prefix the document gives that namespace, or a numbered one.

**A rename reaches what the syntax says names a namespace, and nothing else.** Element names in both
tags, attribute names, markup extension type names at any depth, values that are nothing but
`prefix:Name` lists, attached properties in parentheses, and style selectors. The extension grammar is
the parser's, step for step. Any other mention is left as written and reported (`AXM1044`). A different
default namespace is refused (`AXM1043`), and so is a fragment that is not one well-formed element
(`AXM1042`) — facts about the markup, reported rather than thrown.

**Names follow a third rule, `XamlDuplicateNames.RemoveConflicting`, and it is the default for a
fragment.** Only names the receiving document already declares are taken out, so a control moved
between forms keeps the name its code behind refers to; a duplicate within one document collides on
every name, so the rule is `Remove` there.

## Consequences

- The Surface demo's `Declare`, raw clipboard and `WithoutNames` have a published replacement, and a
  host moving markup between documents writes two editors and one `XamlWorkspace.Apply`.
- Renaming is reserved for the case that cannot be done without it, so most insertions rewrite nothing
  of the fragment but its layout. The price is that a document can end up with a namespace under two
  prefixes; it loads, and costs one attribute.
- A value that is exactly a prefixed name is taken to be one — `Text="local:Badge"` and nothing else is
  renamed with the rest. Telling it from `TargetType` would take what the member means, which this
  package does not know; `docs/limitations.md` says so.
- Recording an insertion where a replacement begins now puts the insertion first in either order,
  which is what lets a declaration and the opening of a self-closing root be one edit. Before, one order
  worked and the other threw.
- What is not rewritten: relative URIs, which need member knowledge; declarations below the fragment's
  element, which Avalonia refused in the source too; and the default namespace.
