# 10. A session says how far an update got, and mutates one thing at a time

Date: 2026-08-04
Status: Accepted

## Context

A hardening review before anything is built on these packages found two promises the code could
not keep.

**`XamlUpdateResult` promised atomicity it could not deliver.** Its documentation said an update
"either lands whole or does not land at all", and the diagnostics on the failure paths said the
objects "were left as they were". Neither is true in general. An update is a run of steps over live
objects, and two of those steps run code this library did not write: a property setter that
validates, and a collection that has to be emptied before it can be refilled. A setter refusing the
third of five values leaves the first two written. A collection that takes back none of what was
moved out of it leaves the original empty. Both were reported as `Applied = false` beside a message
claiming nothing had happened.

The gap mattered because a boolean cannot carry what a caller has to decide. A tool that treats
every `Applied == false` as "undo the document and carry on" is right almost always and, on the day
it is wrong, undoes a document its objects no longer match and goes on editing a tree that
describes neither.

**Mutations of one session could overlap.** The contract asks for them to be "serialized or guarded
explicitly" and nothing did either. `ApplyDocumentUpdateAsync` and `ApplySourceUpdateAsync` are
asynchronous and each reads and replaces `Document`, `Projection`, `Objects`, `PendingDocument`,
the fragment bookkeeping and the object tree. A host watching a folder gets two notifications when
a form and the dictionary it reads are saved together, and starts two updates.

## Decisions

### An update reports how far it got, and the session closes itself when that is not far enough

`XamlUpdateResult.Outcome` is a `XamlUpdateOutcome` with three values — `Applied`,
`RejectedCleanly`, `RequiresNewSession` — and `Applied` is now derived from it rather than set
beside it, so the two cannot contradict each other. Removing `Applied.init` from
`PublicAPI.Shipped.txt` is the release's one breaking change.

`RejectedCleanly` is not a consolation prize; it is the overwhelmingly common case and the reason
the distinction is affordable. Everything that can be checked is checked before the first live
write: the documents are compared, includes are resolved, every fragment is built, every value
converted. A document caught mid-keystroke costs nothing.

`RequiresNewSession` sets `XamlLoadSession.State`, and from then on every mutating operation is
refused with `AXM3043`. Reading still works, because a tool has to be able to show what it was
holding. `PendingDocument` is kept as the document a replacement session should be built from.

**No generic rollback is offered, and none is implied.** The alternative — snapshotting the object
tree and restoring it — cannot be written honestly. What ran were constructors, setters, type
converters, markup extensions and control code, and their side effects reach outside the tree.
Saying so is worth more than a mechanism that works until it does not.

The classification is threaded through every step that reaches a live object, which is what makes
it more than a label. Collections are why: moving content out of a rebuilt copy and into the
original empties one before it fills the other, so a failure between the two is not a refusal
however it is spelled. A collection that refuses *before* losing anything — an items control
reading `ItemsSource` is the everyday one — is told apart by counting it, and stays clean.

`Strategy` keeps its own meaning and answers the other question: what the change *would have*
taken. A rejection carrying `RecreateSession` touched nothing and leaves a working session; it is
the new document that is out of reach. Two axes, because callers act on them separately.

### One mutation boundary per session: the asynchronous updates queue, the synchronous edits refuse

Every operation that changes a session takes one `SemaphoreSlim` for the whole of its work. A
semaphore rather than a lock because it is held across `await` — an update dispatches to the owning
thread and waits — and a monitor lock belongs to a thread rather than to an operation.

`ApplyDocumentUpdateAsync` and `ApplySourceUpdateAsync` **wait**, observing their cancellation
token while they do, and release in a `finally` on every path.

`SetValue` and `SetXamlValue` **refuse**, with `AXM3044`. This is the asymmetry worth recording.
They are synchronous and thread-affine, so waiting means blocking the calling thread — which is the
thread that owns the objects, which is the thread the running update is dispatching to. Waiting
there is a deadlock, not a delay. Making them asynchronous instead would change two shipped
signatures to solve a problem the caller can solve by awaiting the update it already has, and
sync-over-async would reintroduce the deadlock with more steps.

Disposal waits for a mutation in flight rather than cutting it off, because cutting one off is
exactly how objects end up part-way built with nothing left to report it. The semaphore is
deliberately never disposed: a mutation queued behind the disposal has to be able to take the gate
and be told, in this library's words, that the session is gone — rather than be handed an exception
about a semaphore because of when it happened to arrive.

## Consequences

- A caller has to read `Outcome` rather than `Applied` to decide whether to keep a session. The
  migration is in `CHANGELOG.md`; `Applied` still compiles and still means what it meant.
- Enforcing this found a real thread-affinity fault: adopting an updated document rebuilds the
  object map, which reads what Avalonia recorded *on the objects*, and it ran on whatever thread
  the update happened to resume on. That worked for as long as every caller updated from the UI
  thread and failed the moment one did what the asynchronous API invites. It now runs on the
  dispatcher with everything else that touches an object.
- Several reflection and indexer writes that could throw out of `ApplyDocumentUpdateAsync` are now
  diagnostics, which is what the error-handling policy asked for all along.
- The concurrency behaviour is part of the published contract — which of the two things each API
  does — so it is documented per method and covered by tests that use a controllable dispatcher
  rather than delays.
- A future project-system package inherits both: it will drive these sessions from file-system
  events, off the UI thread, several at once, which is precisely the shape both decisions were made
  for.
