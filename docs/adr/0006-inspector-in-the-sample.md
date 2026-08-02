# 6. A property inspector in the sample, and nowhere else

Date: 2026-08-02
Status: Accepted

## Context

The contract's *Out of scope* section names "property-inspector UI" among the things explicitly outside this repository. The list exists for a reason worth restating: these are three libraries, and a designer growing inside them would drag in selection, input, tooling UI and eventually a project system, until the boundaries the whole document is about stopped meaning anything.

The showcase in `samples/` was asked for an inspector: a document loaded as a live preview, a panel beside it listing the selected object's properties, and edits that reach both the running objects and the `.axaml` file on disk.

Read literally, the list forbids it. Read for its purpose, it does not — and the difference is worth being precise about, because the same words will be read again by whoever picks this up next.

## Decision

The out-of-scope list governs the three packages. A sample may demonstrate what a *host* builds on top of them, provided it is built entirely on the public API and adds nothing to `src/`.

The inspector is therefore added to `samples/ArxisStudio.Markup.Xaml.Loader.Sample` and to nothing else. `README.md` gains a sentence saying which scope the list has; the list itself is unchanged.

Three properties make this a demonstration of the boundary rather than a breach of it.

- **It is built on the published surface only.** Members are classified through `XamlLoadSession.GetMember`, edits go through `XamlDocument.Edit().SetAttribute(...)`, and the objects are brought in line through `ApplyDocumentUpdateAsync`. Nothing internal is reached for. If a host could not build this, the packages would be missing something, and the sample is where that would show.
- **The document is what is edited.** An inspector that wrote to the object and then serialised the tree back would violate the first principle in the contract. This one writes the attribute into the syntax tree, and the live objects follow from the update — so "applied to the preview" and "saved to the `.axaml`" are the same act rather than two that have to be kept in step.
- **The rest of the out-of-scope list is untouched.** Selection is a list beside the preview, not a click into it: no adorners are drawn, no pointer or keyboard input aimed at the preview is intercepted, nothing is dragged, and there is no Play/Stop. The preview remains what it was — the objects, drawn, and nothing over them.

A value that is a binding or a resource reference is shown as the expression it is and cannot be edited into a literal. That is the second principle enforced in the one place a user would most expect a tool to break it.

## Consequences

- `samples/` now contains a real `.axaml` file on disk that the showcase reads and writes. It is excluded from Avalonia's XAML compilation, because it is data the sample loads at run time rather than markup the sample is built from — which is the distinction the whole library is about, made visible in the project file.
- The packages gained nothing. `src/` is unchanged by this decision, and the architecture tests continue to say so.
- Anyone reading the out-of-scope list now finds the scope stated with it, rather than inferring a stricter one and refusing work the contract does not actually forbid.
- The inspector shows a property set by a style or a theme as inherited, and writing one adds an attribute to the document. That is an edit to the document, not to the style — the sample says so, because a tool that quietly did the other thing would be lying about where the value came from.
