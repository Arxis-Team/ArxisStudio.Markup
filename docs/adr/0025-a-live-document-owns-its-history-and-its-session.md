# 25. A live document owns its history and its session

Date: 2026-10-02
Status: Accepted. Extends [0007](0007-undo-belongs-to-the-workspace.md) and [0010](0010-a-session-says-how-far-an-update-got.md)

## Context

Every host that shows a document while it changes wires the same four parts together: a workspace
for the history, the document, a session over it, and the rule that keeps the three describing one
text. Wiring them by hand went wrong in the same places each time. The designer sample in
ArxisStudio.Surface kept an undo stack of whole documents beside the workspace's, so the history the
user stepped through was not the one the edits were recorded in; it compared documents by reference
to decide whether a form was changed, so undoing back to the saved text left it marked changed; a
form with unsaved edits skipped a save made by the IDE beside it, and the next save of the form wrote
over the IDE's; and an update the session refused was answered by building a new session whatever
the refusal said — including for a document halfway through being typed, which a new session cannot
load either.

Two smaller defects sat underneath. `MarkupWorkspace.AddDocument` records a step, so undoing far
enough closed the document a tool had just opened. And a file renamed outside the tool could only be
followed by closing the document and opening it again, which threw its history away.

## Decision

**`XamlLiveDocument`, in the loader, is one document with its own history, the text last saved, and
the session that shows it.** It lives in the loader because it holds a session, and nothing below the
loader may. Its history is a `XamlWorkspace` of one document: a designer works on one form at a time,
and a form's history that steps through another form's edits is not what its author expects. The
workspace remains the place where a change that spans documents is one step (ADR 0007); a host that
needs that keeps a workspace of its own and does not use this type for those documents.

**Opening is not a step.** The document is added to its workspace and the history cleared, so the
first undo a user can make is the first thing they did.

**The text is recorded first and shown afterwards, and a change the objects cannot follow is still
recorded.** The session is updated in place where it can follow. Where it cannot, a session is built
from the text. Where no session can be built, the one in place stays if it can still be believed —
its update was refused before anything was written — and the document is `Behind`, with the reason
in its diagnostics; otherwise it is `Broken` and nothing shows it. A text that does not parse is not
built from at all: it is the ordinary state of a file another editor saved halfway through a
sentence, and a session built from a recovered parse would show something nobody wrote.

**Whether a document is changed is a difference of text.** It is compared with the text last saved,
not with a version or a reference, so undoing back to the saved text, or an edit that writes back
what was there, leaves the document clean.

**Text from outside is a step of the history, under a description the host gives, and is what is
saved.** It came from where the document is saved, so after it the document reads as clean — and the
author can take it back, after which it reads as changed. Text that is already the document's — the
echo of the document's own save — only becomes what is saved. Unsaved changes are never overwritten
silently: `ApplyIfClean` reports the conflict and changes nothing, and the host asks the author and
comes back with `TakeTheirs`, which takes the text as a step and leaves the author's one undo away,
or `KeepMine`, which records the outside text as what is saved and leaves the document's own.

**One operation at a time, in the order asked for.** Every operation takes the document's turn —
the same first-come gate a session serialises its updates with (ADR 0011) — and runs against the text
the operations before it left. An edit is recorded through a callback inside the turn, so an edit
queued behind another is computed against the other's result, and an element read from an earlier
version is refused by the editor rather than written where it no longer is. A token gives up waiting
for the turn; once the text has moved, showing it is not cancelled, because a step in the history the
session never heard of is the state this type exists to prevent.

**Events are raised on the thread that owns the objects, once the operation is over.** `Changed` says
what moved — the text, what is saved, the state, the URI — so a handler sees the text, the session
and the state together. `SessionReplaced` is raised while the previous session is still open, because
its root and its map are what the host takes off its canvas; the previous session is disposed once
every handler has returned. The objects are the host's: closing a window the previous session built
is the host's job, as it was before (ADR 0019 closes only the copies a rebuild makes).

**Attaching and detaching move the environment, not the document.** `DetachAsync` disposes the
session and lets go of the environment and the options — a `LocalAssembly`, a root access, resolvers
over a generation of a project's assemblies — and keeps the text, the history and what is saved.
Edits go on landing while a document is detached, and `AttachAsync` shows what the text then says.
Attaching an attached document replaces its session whether or not the new one can be built: the old
session belongs to the environment being replaced, and keeping it would keep that alive.

**A document's identity moves with its file.** `MarkupWorkspace.ChangeUri` moves a document to
another URI as a new version that is not a step of the history, and steps already in it are restored
at the document's current URI rather than the one they were recorded at. `RetargetAsync` uses it and
builds the session again, because what a document includes is found relative to where it lives.

## Alternatives

**A replacement callback on `IXamlRootAccess`.** The interface lends the root around a write; telling
the host its root changed is a different contract, and every host of a live document needs it whether
or not it borrows from the root. `SessionReplaced` carries it, with both sessions, on the right thread.

**One workspace for every live document.** It would keep cross-document undo, and it would make every
form's history step through every other form's — the opposite of what the owner chose for the
designer. A host that wants it builds on `XamlWorkspace` directly.

**Rebuilding on every refusal.** What the sample did. It loads a document that does not parse, fails,
and leaves the host with nothing; it replaces the objects — and with them the selection and the
inspector's subject — for a refusal that a keystroke would have fixed.

## Consequences

- A designer built on these packages has one undo per form, a changed mark that tells the truth, and
  a conflict it is told about instead of one it discovers in the file.
- A change that spans a form and a resource dictionary is two steps in two histories. Recorded in
  `docs/limitations.md`.
- `Behind` is a state a host must show. A document can be behind for as long as its author takes to
  finish typing in the other editor; the session shows the last text that could be shown, and the
  diagnostics say why the current one cannot.
- A host writes through `EditAsync`, not through the session's `SetValue`: the second changes the
  session's document behind the live document's back, and the next update compares against it (the
  rule ADR 0007 states for a workspace, unchanged).
- The history does not outlive the process. A host handing a session to the next copy of itself
  passes the text and what is saved — `Open(uri, text, savedText)` — and the document reads as
  changed from the start, with nothing to undo.
