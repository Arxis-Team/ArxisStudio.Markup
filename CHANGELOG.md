# Changelog

What changed between releases of the three packages, which are versioned and released together.

The three rules in [`README.md`](README.md) are never traded away for any of it: the document stays
the source of truth, an unchanged document round-trips byte for byte, and unknown content survives.

## 0.2.0-preview.1

Milestones 12 to 14, and the hardening a review asked for before anything is built on these
packages. One breaking change, described under *Migrating* below.

### An update says what it did, not just whether it worked

`XamlUpdateResult.Applied` was a boolean, and its documentation promised that an update either
lands whole or does not land at all. The implementation could not promise that for arbitrary user
code, and some of its diagnostics said the objects were untouched when they were not.

- **`XamlUpdateOutcome`** — `Applied`, `RejectedCleanly`, `RequiresNewSession`. The first two leave
  a session worth keeping; the third does not.
- **`XamlSessionState`** and **`XamlLoadSession.State`** — a session that stopped part-way through
  an update says so, and refuses every later mutation with `AXM3043` rather than writing onto a
  tree that describes nothing. Reading it still works.
- **`AXM3043`** `SessionRequiresRecreation` and **`AXM3044`** `SessionBusy` are new.
- `PendingDocument` is now also what a replacement session should be built from, not only what was
  refused.
- Every path that reaches a live object — property writes, reorders, dictionary and list
  replacement, content moves, fragment rebuilds, design-value reapplication, cancellation — was
  audited and classified. Collections drove most of it: moving content out of a rebuilt copy and
  into the original empties one before it fills the other, and a failure in between is not a
  refusal. Where a collection refuses *before* losing anything — an items control reading
  `ItemsSource` — that is told apart by counting, and stays a clean refusal.
- Several reflection and indexer writes that could throw out of `ApplyDocumentUpdateAsync` are now
  reported as diagnostics, which is what the error-handling policy asks for.

### One session mutates at a time

`XamlLoadSession` had asynchronous update paths that could overlap while reading and replacing
`Document`, `Projection`, `Objects`, `PendingDocument` and the object tree itself — which is what a
host watching a folder gets when a form and its dictionary are saved together.

- Every mutating operation now passes through one gate per session.
- `ApplyDocumentUpdateAsync` and `ApplySourceUpdateAsync` **queue**, observing their cancellation
  token while they wait. The gate is released in a `finally` on success, failure and cancellation.
- `SetValue` and `SetXamlValue` **refuse** with `AXM3044` rather than wait: blocking there could
  block the thread the running update is dispatching to, which is a deadlock rather than a delay.
- `DisposeAsync` waits for a mutation already in flight instead of cutting it off, and is now
  genuinely asynchronous. Work arriving afterwards gets `ObjectDisposedException`.
- Reading — the object map, `GetMembers`, `GetValueInfo` — takes no lock.

### Fixed

- **Adopting an updated document touched Avalonia objects off the owning thread.** Rebuilding the
  object map reads what Avalonia recorded on the objects themselves, and it ran on whatever thread
  the update happened to resume on. That worked for as long as every caller updated from the UI
  thread and failed the moment one did what the asynchronous API invites — call it from a file
  watcher. It now runs on the dispatcher, like everything else that reaches an object.

### From milestones 12 to 14, first released here

- **Identity and reordering** — an element that declares `x:Name`, or `Name` where it means the
  same, is paired across an update by it, and reordered siblings move rather than being rebuilt.
- **`XamlWorkspace`** — structured edits applied through the workspace, so one edit is one undo
  entry under a name a user would recognise, and an edit spanning two documents is one action.
- **`ReplaceElement`, `WrapElement`, `UnwrapElement`, `DuplicateElement`.**
- **`XamlElementPath`** — a reference to an element that survives an edit, an undo and a redo.
- **`XamlElement.ContentElements`, `MemberElements`, `Identity`, `IndexInContent`** — the rules
  every host was re-deriving, published. An insertion index counts content children, so index 0 in
  a parent that declares a property element means before its first content child.
- **`XamlLoadSession.GetMembers`, `XamlMemberResolver.Enumerate` and `FindContent`** — a tool can
  ask a type what it has instead of keeping a table of names, and where unnamed children go is read
  from Avalonia's `[Content]` attribute rather than from a list of framework base types.
- **`XamlMemberDescriptor.ConvertFromText`** and `XamlValueConversionResult` — the same conversion
  a write does, askable before anything is written.
- **`XamlLoadEnvironment.MemberResolver`** — descriptors are cached per environment rather than per
  process, so rebuilding a control library and loading it again starts clean.

### Migrating

**`XamlUpdateResult.Applied` is no longer settable.** It is now derived from `Outcome`, so the two
cannot disagree. Nothing outside these packages constructs an `XamlUpdateResult`, so this affects
only code that did:

```csharp
// before
new XamlUpdateResult { Applied = true, Strategy = …, Changes = …, Diagnostics = … }

// after
new XamlUpdateResult { Outcome = XamlUpdateOutcome.Applied, Strategy = …, Changes = …, Diagnostics = … }
```

**Reading `Applied` still compiles and still means what it meant.** But a tool that treats every
`Applied == false` as "undo the document and carry on" now has a case to add: after
`RequiresNewSession` the objects have moved and undoing the document is a lie. The three-way form
is in [`docs/api/updates.md`](docs/api/updates.md#a-tools-update-loop).

**`DisposeAsync` may now actually await.** It waits for a mutation in flight. Code that dropped the
returned `ValueTask` was already wrong and is now wrong visibly.

**Packages carry Source Link and symbols.** Nothing to do; stepping into these packages now lands
in the commit they were built from.

## 0.1.0-preview

Milestones 0 to 11: the text model, transactions and undo, the lossless lexer and parser, values
and editing, the resource graph, the Avalonia loader, runtime mapping and properties, `x:Class` and
events, resources, styles and templates, design mode and updates, and stabilisation.

Every item under *Definition of done for the first preview release* in [`README.md`](README.md)
holds from this release onwards.
