# 24. A placed class is constructed by the session, and rebuilt on request

Date: 2026-10-02
Status: Accepted. Extends [0014](0014-instances-of-compiled-controls-populate-from-live-documents.md) and [0015](0015-a-session-populates-an-x-class-root-inside-its-constructor.md)

## Context

Avalonia's runtime loader treats the root of what it is handed as the thing the text defines. For a type
with compiled markup it installs the text as the populate the type's constructor runs, and writes `null`
into that hook afterwards (ADR 0015). For a document's own root that is right: the document is the
definition. For a part an update rebuilds it is not. A control placed on the form — `<local:CustomerCard
Tag="{x:Type Button}" />`, rebuilt because nothing writes that expression in place — is the root of its
part, and Avalonia populated it from the part: markup that places the card and says nothing about its
content. The card came back empty. And the `null` written into the hook ended the live registration
for the card's class, so every card constructed afterwards showed its compiled markup.

A designer also needs the opposite of an update. When a control's own markup changes, the form that
places it does not; the instances on the form go on showing what the control's markup said when they
were built. The designer sample found them by the class's simple name and loaded the whole form again.

## Decision

**A part whose root is not the document's root, and whose type has compiled markup, is constructed by
the session.** The type is resolved before the update's turn, with the projections; in the turn, the
session constructs it with its parameterless constructor — the constructor populates it from its
compiled markup or from a live document registered for it — and hands Avalonia the part to load onto
that instance. A constructor that throws is the update's failure, reported in the constructor's words.

**`XamlLoadSession.ApplyRebuildAsync(elements)` builds elements again as they stand.** Each is rebuilt as
a change to it would be — the element, or the smallest container around it, together with whatever
around it a static reference inside it reads (ADR 0020) — through the same path as an update, in the
same gate. The elements are of the session's document when the rebuild's turn comes: one an update has
since replaced is refused with nothing written, and the root, which the session is built around, is
refused with `RecreateSession`.

## Consequences

- A placed control rebuilt by an update shows its own markup, and a live registration for its class
  outlives the rebuild.
- A host following a control's markup registers the new document with `XamlLivePopulation`, finds the
  elements whose objects are of the class, and asks for them to be rebuilt; the form's other objects
  are untouched, and the session is the same.
- Which elements depend on which class is the host's question — it knows what changed — and the session
  answers by object, not by name: `GetObject(element)` is of the class or it is not.
