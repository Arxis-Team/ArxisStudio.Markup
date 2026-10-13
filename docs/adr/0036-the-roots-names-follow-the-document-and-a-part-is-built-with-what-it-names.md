# 36. The root's names follow the document, and a part is built with the elements it names

Date: 2026-10-13
Status: Accepted. Extends [0020](0020-a-rebuilt-part-carries-its-scope.md) and [0021](0021-values-and-expressions-are-written-where-they-stand.md)

## Context

A designer binds a text block to a slider by name in one edit: the slider is given `x:Name="slider"` and the text
block `Text="{Binding #slider.Value}"`. The document was right, the project built, and the form went on showing the
old text until it was loaded again. The loader had no notion of a name scope, and three parts of the update path
knew nothing of names.

**The root's scope stood still.** A load registers every name of the document's own scope in a `NameScope` it puts on
the root, and completes it — after which Avalonia takes no registration and has no way to take one back. A name an
update wrote was never registered, one it took off stayed, and an element an update built again was registered as
the object it replaced.

**A binding written in place had no scope.** ADR 0021 writes a reflection `{Binding}` where it stands, built through
the binding's public setters. A binding that reads an element by its name finds it through a name scope the binding
keeps internally, and a load hands it one through the service provider its extension is given. Built without one,
`#slider.Value` reported "NameScope not found" and showed nothing, and `ElementName=slider` threw out of the write and
left the session describing nothing.

**A part is a load of its own.** A part an update rebuilds is loaded from its own text, and the runtime loader gives
every load a scope of its own — it makes a new one and asks nothing it is given for it. A compiled binding that names
an element outside the part is refused by Avalonia's compiler ("Unable to find element 'slider' in the current
namescope"), before any scope exists, and a reflection binding finds nothing. The other direction was quieter: a
binding outside a part that names an element the part builds again goes on reading the object that was replaced. And
a part that rebuilds only its content is built from a copy of its root, so a binding inside it that names the root
read the copy.

## Decision

**The root's scope names what the document names.** Once an update's objects are written and the map follows them —
in the same dispatcher turn — a root whose names moved, because one was written, taken off or stands for another
object now, is given a new `NameScope`: every name of the document's own scope with the object the map pairs its
element with, completed as a load leaves one. The document's own scope is its elements outside templates, whose
content has a scope of its own — the rule ADR 0033 reads a binding's source by, and now the one place it is written.
`Name` is a name as `x:Name` is, and written or changed it builds its element again, like the directive: Avalonia
refuses a name on an element it has styled, and writing it in place broke the session. The scope the load made is
kept for the session's life — bindings hold their scope weakly, and a style the load built makes its bindings afresh
each time it applies.

**A binding written in place that reads an element by its name is built as a load builds it.** The
`ReflectionBindingExtension` carrying the written arguments is asked for its binding with a service provider that
answers `INameScope`, which is the public road the load takes. The scope is the one the update leaves on the root:
made before the turn and filled in it, so a name the same update writes is found as it is registered.

**A rebuild is moved out to the nearest element whose content holds both a binding that reads an element by its name
and the element it names**, wherever the part would build one of the two and not the other — the way ADR 0020 moves it
out to the element that answers a static key. A part that rebuilds only its content stands apart from whatever names
it and whatever it names: its object may stay, and a binding inside that names it reads the copy; or it may yet be
replaced, once the update finds a member written as a property element that only a new object can take, and then its
own bindings are built in the part and one outside that names it reads what was replaced. A binding the same update
writes in place outside the part is none of the part's — it reads the update's scope, and one that cannot be written
there is rebuilt and reaches out on its own. Where only the root holds both — the root reads a name, or a part reads
the root by its name — the update is a new session: the root is what a session is built around, and a part built
without it reads a copy, or nothing.

**A step out is asked again.** What a step out brings in reads keys and names of its own, so the reach is worked out
again from the element stepped out to, for keys and names alike, until the part answers for everything it holds. ADR
0020's single step left a key read by what the step brought in to a part that did not declare it.

**The part is not given the root's scope.** That was the obvious alternative, and Avalonia leaves it closed: the
runtime loader makes the scope of every load itself, and a compiled binding is refused when it is compiled, before
there is a scope to give.

## Consequences

- A name written on an element, taken off it, or written as `Name`, is found in the root's scope after the update —
  and gone from it — and code that asks the root for a control by its name gets the one on screen.
- `{Binding #name…}` and `{Binding …, ElementName=…}` written in place show the element's value, a name the same
  update wrote included; `ElementName` no longer breaks the session.
- A rebuild can be larger than the change that asked for it: a text block given a compiled binding to the slider
  beside it rebuilds the panel that holds both, and a named element read from elsewhere is rebuilt with what reads it.
  A part that reads the root by its name, or that the root reads by name, costs a new session.
- A style, a theme, a template and a keyed resource are rebuilt whole and are not moved out for a name. A binding in
  one that names an element outside it is built as before, and a binding the load built in a style goes on reading
  the load's scope.
