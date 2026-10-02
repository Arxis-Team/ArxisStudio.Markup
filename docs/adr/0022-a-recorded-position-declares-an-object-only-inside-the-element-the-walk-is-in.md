# 22. A recorded position declares an object only inside the element the walk is in

Date: 2026-10-02
Status: Accepted

## Context

The object map pairs an object with the element that declared it by where Avalonia recorded building it
— a source URI and a position, read through the projection back to the document. Two facts made that
reading wrong around controls with markup of their own.

A control written with `x:Class` and placed on a form — `<local:CustomerCard />` — populates itself from
its own markup in its constructor, and Avalonia records *that* markup's position on the instance. The
map refused a position from another document, rightly, and the refusal left the control mapped to
nothing: a click on it in a designer selected the form, and deleting it had nothing to delete.

And Avalonia's runtime loader names every text it is handed alike. The document, each part an update
builds and the markup live population builds a placed control from are all recorded under one URI, so
the URI cannot say which text a position is in. A placed control populated from its live document had
its inner text block read as a position in the form, and paired with the control's own element or with
a sibling three lines down.

## Decision

**A recorded position counts only where it falls strictly inside the element of the parent the walk is
in.** The walk goes down the logical tree with the element its parent was paired with; an object whose
recorded element lies outside that element — or is that element — is somebody else's markup, and is
left unpaired by position. A control placed on the form is paired by where it stands among its parent's
children, the way every unnamed child is, and what its markup built inside it belongs to no element of
the form.

**An object an update carried across or rebuilt is declared by the element the update paired it with**,
which is better evidence than a position recorded against a text that may not be this one.

**A synchronous edit keeps the map whole.** `SetValue` rebuilds the map carrying every pair it had —
objects an earlier update rebuilt included — and the fragments those objects came from. A source update
compares only what the includes contributed to the projection, so a document the session itself wrote
is not read as a changed include.

## Consequences

- A placed control is selectable and deletable in a designer, whether its markup is compiled or live.
- The pairing no longer depends on the URIs Avalonia records being distinct, which they are not.
- What a placed control's own markup built is not reported as anything's; a host that wants to show it
  loads that markup in a session of its own.
