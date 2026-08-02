# 7. Undo belongs to the workspace, and edits go through the document

Date: 2026-08-02
Status: Accepted

## Context

Two questions came out of auditing the packages as a foundation for a visual designer: should
undo/redo live in these libraries or in the tool built on them, and which of the two ways of
writing a change is the one a tool should use.

Both were already answered by the code, and neither was written down — which is the same as not
being answered, because the audit found the evidence that nobody had followed either answer:
`MarkupWorkspace.Undo` was reachable only from the base package's own tests, and the showcase's
inspector wrote properties through the document while `XamlLoadSession.SetValue` sat there doing
exactly that job in one call.

## Decision

### Undo/redo stays in `ArxisStudio.Markup`

The unit of undo is a change to a document's text, which is that package's model and nothing
else's. More importantly, one thing a user does can touch several files — moving a control and
changing the resource dictionary it reads from — and undoing half of that leaves the two
disagreeing. Atomicity across documents is a property of the set of open documents, so it belongs
to whatever holds that set. A designer should not reimplement it, and would get it wrong in the
same way every tool that keeps a stack of whole-file snapshots gets it wrong.

`XamlWorkspace` in `ArxisStudio.Markup.Xaml` is the seam: it takes a `XamlDocumentEditor`, turns
its edits into a transaction with a description, and hands back the reparsed document. One
structured edit is one entry in the history, named for what the user did.

The seam is in the XAML package rather than the base one because it knows what a `XamlDocument`
is, and the base package must not.

### A tool writes through the document, not through the object

There are two directions, and both are correct:

- **From the document.** Record edits on a `XamlDocumentEditor`, apply them through
  `XamlWorkspace`, and let `XamlLoadSession.ApplyDocumentUpdateAsync` bring the objects into line.
- **From the object.** `XamlLoadSession.SetValue` sets the property and writes the document in one
  operation, putting the object back if the write fails.

A tool with a history uses the first, because only it passes through the workspace and therefore
only it can be undone. The second exists for a host that has no workspace — a previewer, a test, a
script — and its documentation now says that it creates no undo entry.

They must not be mixed on the same document: the session's document is not the workspace's, and a
`SetValue` would advance one while the other stood still.

## Consequences

- A designer built on these packages inherits multi-document, transactional undo and does not
  write one.
- `XamlWorkspace.Apply` refuses an editor opened on a version the workspace has moved past. Its
  changes are spans into text that no longer exists, and applying them would cut the document in
  the wrong places; approximating that would be worse than refusing.
- The current parse of each open document is cached, one per document rather than one per version.
  Undoing costs one parse. Holding every version a session ever had would grow without bound, and
  a bounded cache that is occasionally cold is the better trade.
- `SetValue` is not deprecated. Removing it would leave a host without a workspace no way to write
  a property atomically, and that is a real shape.
