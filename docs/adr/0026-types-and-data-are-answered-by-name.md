# 26. Types and data are answered by name

Date: 2026-10-02
Status: Accepted. Extends [0009](0009-member-resolution-belongs-to-the-environment.md)

## Context

A designer asks three questions these packages could not answer, and every host answered them by
reflecting over `Type` objects it then kept: which controls an assembly offers and under which name a
document writes each, what the bindings written on an element bind to, and whether a binding path
still resolves after the code it names has changed.

Keeping the types is the defect. A designer loads a project's assemblies into a context it can unload,
replaces that context when the code is built again, and has to prove the old one is gone before it
loads the new one. A toolbox, a data panel or a cached member list that holds a `Type`, a
`PropertyInfo` or an `AvaloniaProperty` of the old generation holds the whole generation, and the
proof fails with nothing to show for why.

## Decision

**What a tool keeps is names.** `XamlTypeCatalog` reads the types a set of assemblies offers a
document into `XamlTypeEntry` records — full name, name, CLR namespace, assembly, the XML namespace a
document writes it in, the prefix the library suggests for that namespace, and what kind of type it is
— and holds neither the types nor the assemblies. `XamlMemberResolver.EnumerateBindable` lists what a
binding can read on a source as `XamlBindableMember` records — name, the type's name as a reader writes
it, and whether it can be written, holds a collection, or holds a command.

**What a tool asks in passing may answer with a type, said so.** `ResolveBindingPath` returns the type
a path ends at, and `XamlLoadSession.GetDataContextAsync` the data type in scope and the design data's
type, because the next question — the members of that type, a step further down the path — needs it.
Both say in their documentation that the type belongs to a generation, and that a tool reads what it
needs and lets it go.

**A type is written where the loader would find it.** The catalog's namespace for a type is the first
one its assembly maps the type's CLR namespace to with `XmlnsDefinition`, read the same way
`XamlTypeResolver` reads the mapping when it resolves a document — the two share one reading of the
attributes, `XamlNamespaceAttributes`, so they cannot disagree about where a type lives — and
`using:` and the CLR namespace where the assembly maps none. The prefix is the one `XmlnsPrefix`
suggests for that namespace. Nothing caches the attributes in a static: an assembly may be in a
context that is meant to unload, and each caller caches for as long as it lives.

**A kind is a question a tool asks, answered from the type once.** `XamlTypeKinds` says whether a
document can write the type as an element, whether it is a control and of which shape — panel,
content control, decorator, items control, templated, user control, top level — whether its own
markup was compiled into it, and whether it is data rather than an Avalonia object. Which of them a
toolbox offers is the toolbox's decision.

**A path is read as a binding reads it, as far as types go, and never claimed broken beyond that.**
Dotted members, integer and string indexers and a leading `!` are followed. A step whose type is
`object` or is resolved at run time, an attached property in parentheses, a cast, `$parent`, `#name`
and the rest are `NotUnderstood`: the binding may well work, and an indicator that called it broken
would be wrong about the projects that rely on it. `Broken` is said only where a step names a member
the type does not have.

**The data type in scope is the nearest `x:DataType`, written as a type name or as `{x:Type}`, resolved
through the environment** — the resolver a load uses. Whether bindings compile there is the nearest
`x:CompileBindings` that reads as a truth value, and the session's default where none does. The design
data is whatever the element's object holds as its data context, read on the owning thread.

## Consequences

- A designer can hold its toolbox and its data panel across a replacement of a project's code; what it
  must release are the answers it asked for in passing.
- A catalog is a reading, not a view: a build that adds a control is a new catalog.
- `NotUnderstood` is a third state a binding indicator has to show — as "not checked", not as a break.
- The member list is per environment, like every other member cache (ADR 0009), and goes with the
  environment it was answered in.
