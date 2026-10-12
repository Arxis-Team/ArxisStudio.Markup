# 34. An argument is written where it stands

Date: 2026-10-13
Status: Accepted

## Context

A tool that edits a binding — its path, its mode, its converter — changes one argument of an expression the
document already has. The model gave it two ways to do that and both lost something. Writing new text by hand
meant parsing the old one again, and an expression built from `Arguments` renders every argument anew: the
document's spacing and order go, and a quoted literal comes back without its quotes. A format such as
`StringFormat='{}{0:N2}'` or a fallback holding a comma was written back as text the next parse reads as a
nested extension or as two arguments. The first edit to a binding rewrote all of it.

## Decision

**`XamlMarkupExtensionValue.WithArgument` sets one argument and splices the written text.** The parser notes
where each argument stands in `RawText`; an edit replaces that span, removes it with its separator, or adds an
argument — a positional one before the named ones, the only place an extension takes it, a named one last.
Every argument the edit did not touch keeps its text exactly. A value constructed rather than parsed has no text
to keep and is written anew from its arguments. The result is parsed from the new text, so edits chain.

**A literal is quoted when a bare argument would misread it**: a comma or a closing brace would end it, an
opening brace would start a nested extension, an equals sign would make a positional value a name, white space
at an end would be trimmed, nothing at all would vanish. It goes in single quotes, or in double quotes when it
holds an apostrophe — the document's writer escapes a quote that would end the attribute. A quoted argument has
no escape for a quote, so a literal holding both kinds cannot be written, and the edit refuses it rather than
write what does not read back.

**Quotes do not keep a leading brace from opening an extension.** Avalonia's loader reads
`StringFormat='{0:N2}'` as the extension `{0:N2}` and fails on its type; `StringFormat='{}{0:N2}'` gives the
format. So a quoted literal that starts with a brace is written with the `{}` escape inside the quotes, and the
parser drops a leading `{}` from a quoted argument as it does from a whole attribute: the escape is how the value
is written, not part of it. `XamlMarkupExtensionArgument.ToString` quotes the same way, which
makes an expression built from arguments render as text that parses back to them.

## Consequences

- A tool changes a binding's mode without touching its converter, and a document diff shows the one argument.
- The argument spans are the parser's and are not public: they describe `RawText`, and a value whose text was
  set by hand is written anew.
- What a bare argument holds is what it said; a literal the document quoted for no reason stays quoted until
  that argument is the one being set.
