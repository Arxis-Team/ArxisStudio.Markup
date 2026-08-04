# 11. What a review found in the mutation boundary

Date: 2026-08-04
Status: Accepted

Amends [ADR 0010](0010-a-session-says-how-far-an-update-got.md), which stands except where this
says otherwise.

## Context

ADR 0010 gave a session one mutation boundary and an honest three-way update outcome. A review of
that work found four things wrong with it. All four are cases where the code was *nearly* right,
which is why the tests written alongside it passed.

## Decisions

### The definitive checks belong inside the boundary, not before it

`SetValue` and `SetXamlValue` asked whether the session was still usable and *then* took the gate.
Between those two moments an update that already owned the gate could fail after writing, mark the
session, and let go — and the edit would take its turn on a session that by then refused
everything. The same shape applied to disposal.

Both checks now happen with the lease held, and the answer read on the way in is not consulted at
all. `_disposed` also stopped being a plain `bool`: disposal and the mutations it races are on
different threads by design, so the flag is an `int` read and written through `Interlocked`, which
is also what makes disposing twice safe rather than accidentally safe.

There is no test that drives the window itself — from outside the session there is no seam between
"read the state" and "take the gate", and inventing one would mean shipping API to test a
guarantee. What is tested is the guarantee: after an update breaks the session, and after disposal
begins, neither editing method applies.

### A failure after a write keeps the document the caller is told to recover from

`docs/api/updates.md` tells a caller whose update stopped part-way to build a new session from
`PendingDocument`. The path that marked the session after a post-write failure — cancellation
included — did not set it, so the documentation described a recovery that was sometimes impossible.

It is now set wherever the session is marked, and once set it is not replaced: a later update that
arrives and is refused has no claim on the answer to "what should I load instead", because the
objects are part-way towards the first document and not towards that one. It is cleared only when a
whole update is adopted.

### An invoked setter that throws is never a clean refusal

This reverses a judgement made in the previous round, and the review is right.

The old rule was that a setter refusing a value leaves the object as it was, so the first such
refusal in an update was clean. That holds for a property that validates before assigning, and
Avalonia's `validate` callback is exactly that — which is what made it look general. It is not.
This is legal, and a control library is free to write it:

```csharp
set
{
    _value = value;
    Tag = Describe(value);
    throw new InvalidOperationException("…and now I am unhappy.");
}
```

Once user code has run, what it did first is unknowable, and comparing the written property
afterwards proves nothing — the example above changes a *different* property. So: **a refusal has
to be reached without running the object's own code.** Everything checkable is still checked first,
and that is where clean refusals come from — the member exists, it can be written, the text
converts, the collection reports itself read-only. Past that point a failure is
`RequiresNewSession`.

Two things keep this from being ruinous in practice. Avalonia answers `IsReadOnly` on an items
control's `Items` once `ItemsSource` is bound, so the everyday case a designer meets is refused
before anything is invoked and stays clean. And a write to a **rebuilt copy** — an object this
update built and is about to throw away — is still a clean failure whatever it does, which
`XamlObjectExposure` is for.

The cost is real and is recorded in `docs/limitations.md`: a control whose setter throws now costs
the session rather than the edit. That is the honest price of not being able to prove otherwise,
and the conversion check in front of it means a typo in a property field never reaches a setter.

### The queue is a queue

`SemaphoreSlim` gives mutual exclusion and says nothing about the order it releases waiters in.
ADR 0010 documented that updates "queue", which was a promise the implementation did not make. For
a host watching files, three saves whose oldest is released last means a preview showing text from
two saves ago, with every mutation perfectly serialised.

`XamlMutationGate` replaces it: order assigned when the request arrives, ownership handed straight
from the leaving operation to the next waiter rather than dropped for whoever wakes first, a lock
held only long enough to move a link in a list and never across an `await`, a lease that releases
exactly once, and a non-blocking `TryEnter` for the synchronous edits that also refuses when
somebody is *waiting* — an edit unwilling to stand in the queue does not get to walk past it.

Cancellation removes a waiter from the middle of the queue and can never release ownership it
never held. A waiter granted its turn and cancelled in the same breath gives the turn back before
throwing, which is the one place ownership and cancellation genuinely race.

It is internal. Nothing about it needs to be public, and a public async gate would be a second
thing to support for no caller's benefit.

## Consequences

- Disposal is now the only thing that waits on the gate without a token, which is deliberate: it is
  waiting for work that must be allowed to finish.
- A queued update that runs after disposal begins throws `ObjectDisposedException` rather than
  running against a disposed session, and disposal does not deadlock behind it.
- The conservative rule made one previously passing test wrong, and it was rewritten rather than
  weakened: a setter throwing on the very first write of an update now requires a new session.
- The test controls grew the failure shapes real controls have — throw before assigning, assign and
  then throw, change something else and then throw, empty half a collection and then throw — because
  the rule cannot be justified against a control that only fails the polite way.
