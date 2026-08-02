# 9. Member resolution belongs to the environment, and conversion is askable

Date: 2026-08-02
Status: Accepted

## Context

An architectural review of the packages as a foundation for visual editors asked whether it is
right that a tool gets a control's properties from these libraries at all. It is — the
classification is what an edit already depends on, and Avalonia's property system is exactly the
knowledge a tool should not re-derive: `GetRegistered` answers styled and direct properties for a
type and its bases, attached ones live in `GetRegisteredAttached` under the type they attach *to*
and are written `Owner.Member`, and some are registered both ways.

Two things were wrong with how that knowledge was reached.

**The cache outlived what it described.** `XamlMemberResolver.Instance` is a process-wide singleton
keyed by `Type`. An IDE-like tool rebuilds the user's control library and loads it again; a static
cache pins those types against a collectible `AssemblyLoadContext` that was meant to be unloaded,
and keeps answering with descriptors of a build that no longer exists.

**Conversion was invisible.** `XamlValueConversion` was internal, so a tool could not ask whether
what the user has typed is a value the member can hold. Its only way to find out was to write the
attribute, let the update refuse it, and roll back — a history entry and a failed update for a
half-typed number.

## Decision

### `XamlLoadEnvironment.MemberResolver`

Member resolution is a service of the environment, alongside the assembly, type and resource
resolvers, and defaults to one resolver per environment. What it caches is CLR metadata about the
assemblies that environment resolves, so it is discarded with the environment. A caller may supply
its own to share a cache between environments that resolve the same assemblies.

`XamlMemberResolver.Instance` stays for a caller with no environment — the same shape as
`XamlLoadSession.SetValue` staying for a host with no workspace in ADR 0007 — and nothing inside
the packages uses it any more.

### `XamlMemberDescriptor.ConvertFromText`

The conversion an update performs, asked in advance and without writing anything. It returns
`XamlValueConversionResult`: the value, or a sentence saying what is wrong with the text. The
update path, `SetXamlValue` and design-time values all go through the same method, so what a field
says while it is being typed and what the document will do cannot drift apart.

Assignability is part of it. A converter is free to answer with something else entirely, and a
value of the wrong type reaches an Avalonia setter as an exception; checking inside the conversion
is what keeps that an ordinary refusal.

## Consequences

- A tool that reloads assemblies builds a new environment and gets a clean answer; the old
  environment and everything it knew go together.
- Sharing a resolver is a decision the caller makes explicitly rather than one the library makes
  for everybody.
- An inspector validates a field with one call and no side effects. The showcase does exactly
  that: text a member cannot hold is refused before the document is touched, so it never reaches
  the undo history.
- Text that parses as a markup extension is not a value and is not converted — writing
  `{Binding Customer.Name}` into a field still writes a binding, which is a load-time question.
- Threading the resolver through the internal statics that need it (`XamlObjectReplacement`,
  `XamlDesignValues`, `XamlAttributeChecks`) is the cost. It is also an improvement: those were
  reaching for global state from the middle of an update.
