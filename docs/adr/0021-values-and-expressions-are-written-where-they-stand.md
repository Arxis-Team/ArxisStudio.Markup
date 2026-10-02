# 21. Values and expressions are written where they stand when a load would need nothing else

Date: 2026-10-02
Status: Accepted

## Context

An update set literals in place and treated everything else as structural: a removed attribute, every
markup extension, every change to `mc:Ignorable`. Structural at the root means a new session, and a
new session for a form whose class resolved means the class constructed again. A property inspector
writes exactly these: it binds a property to the data context, points a brush at a dynamic resource,
resets a value to its default — and each cost the form a new instance and the designer a reload.
Writing the first design value cost the same, because the tool declares the design namespace and lists
it in `mc:Ignorable` along with it.

Two more things were wrong on the way. An update that replaced a binding with a literal set the literal
and left the binding running, so the next change of the source wrote over what the document said —
`SetValue` had been fixed for this, the update path had not. And the order of `XamlUpdateStrategy` is
the order of cost, which the update path compares: a new strategy belongs where its cost is.

## Decision

**A removal clears the local value where it stands** (`ClearProperty`), for an Avalonia property —
attached ones included, and the root's. A CLR property has no local value to take away, so its removal
rebuilds the element.

**An expression a load would evaluate with nothing but its element to go on is set where it stands**
(`SetExpression`): `{x:Null}`; `{x:Static prefix:Type.Member}`, a public static field or property whose
value fits the member; `{DynamicResource Key}`, as the binding Avalonia makes of it; and a `{Binding}` —
or `{ReflectionBinding}` — with `Path`, `Mode`, `StringFormat`, `ElementName`, `RelativeSource` (`Mode`,
`AncestorType`, `AncestorLevel`), `FallbackValue` and `TargetNullValue`. A `{Binding}` where bindings
compile — `x:CompileBindings="True"` above it, or `UseCompiledBindingsByDefault` with nothing in scope
saying otherwise — is not set in place: a load checks a compiled binding against its data type, and a
reflection binding set in its place would accept a path the load refuses. Everything else rebuilds: a
static resource, a converter, `{CompiledBinding}`, an argument not listed.

**Whether a change can be written in place is decided before anything is projected**, in an asynchronous
pass over the changes, against the object's member as the environment resolves it. A change that cannot
be is turned into the rebuild a load would need — its element, the smallest container around it, and at
the root a new session — before its fragment is projected; a change found unwritable once writing had
begun would be a broken session rather than a rebuilt element.

**Every write in place ends the binding the property had first.**

**`mc:Ignorable` that only gains namespaces nothing in the loaded document used is no change.** A reader
skips markup in an ignorable namespace, and there was none. A namespace taken off the list, or one the
loaded document has markup in, is structural as before.

**The new strategies sit after `SetProperty`**, where their cost is, and every member after them moves
up by two. The enum is public, so the move is breaking and the changelog says so.

## Consequences

- A binding, a resource reference or a reset from an inspector costs a write; at the root, it no longer
  costs a new session. The first design value no longer either.
- The in-place path builds the same Avalonia objects a load does — `ReflectionBinding`,
  `DynamicResourceExtension`, `RelativeSource` — through their public API, and anything it would have
  to guess about is a rebuild. A rebuild is always right.
- Code that stored `XamlUpdateStrategy` values as numbers sees them shifted.
