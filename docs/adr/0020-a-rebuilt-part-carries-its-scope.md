# 20. A rebuilt part carries its scope

Date: 2026-10-02
Status: Accepted. Extends [0019](0019-a-rebuilt-part-is-built-without-the-class-and-its-handlers-are-hooked-up-to-the-root.md)

## Context

A part an update rebuilds is projected from its own element and loaded on its own. Three things the
element's markup means in the document it does not mean on its own, and each was a part rebuilt wrong
or not at all.

**What the element's ancestors say about bindings.** `x:DataType` and `x:CompileBindings` are written
once, near the root, and apply below. A part under them lost both: a compiled binding in it had no data
type to compile against, and Avalonia refused the part — so a child added under a form with compiled
bindings cost a new session.

**The dictionaries a static reference reads.** `{StaticResource Accent}` is resolved while the part is
built, against the dictionaries in scope where the markup sits. A part built on its own has none of the
dictionaries above it, so a text block given a foreground from the form's resources was rebuilt with no
brush at all and the update reported success. The opposite direction was already handled — a changed
resource rebuilds the element that declares it — and this is the same fact read from the reader's end.
A key the document does not declare may still come from a file an element above includes, and which
file says what is a question the syntax cannot answer.

**Another rebuild of the same update.** Two rebuilds, one inside the other — a part rebuilt for a
static reference, around a panel that gained a child — were both built and both applied. Applied after
the outer one, the inner one put its copy into a tree the outer one had already replaced, and the map
then named the copy: a selection on it pointed at nothing on screen, and a handler hooked up to it never
ran. Applied before, its copy was thrown away.

## Decision

**The nearest `x:DataType` and `x:CompileBindings` above a part are written onto its root**, as their
text in the document, unless the part's root writes its own. They are carried by the projection, beside
the namespace declarations a part already carried, and the document is not touched.

**A rebuild that reads a key from outside itself is moved out to the element that answers it.** For
every key read with `StaticResource` in the part — in an attribute, nested in another expression, or as
an element — the dictionaries are walked from the reader up, as the lookup walks them: an element's
`Resources` and its `Styles`. The nearest declaration is the one the lookup finds; inside the part it
needs nothing, outside it the rebuild becomes a rebuild of what the declaring element holds. A key no
element declares, where an element above includes another file, moves the rebuild out past the
outermost such element, because any of the files may hold it. The widest answer over all keys wins.

**A rebuild covered by another is not built.** Of the rebuilds an update asks for, one inside another's
element is left to the other — what the outer one builds is the inner part as the document says — and
of two of one element, the one that replaces the object covers the one that rebuilds its content.

## Consequences

- A structural change under compiled bindings applies in place; a part that reads a key from the form's
  resources, or from a file the form includes, shows the resource after it is rebuilt.
- A rebuild can be larger than the change that asked for it — the root's content, for a key the root
  declares. That is the contract's rule: where either of two would plausibly do, take the larger.
- Only keys written as text are followed. A key written as an expression is not, and its part is rebuilt
  where it stands.
- The same coverage applies to a source update, whose hosts inside a rebuilt root are now built once.
