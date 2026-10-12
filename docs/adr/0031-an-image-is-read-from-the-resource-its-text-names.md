# 31. An image is read from the resource its text names

Date: 2026-10-12
Status: Accepted

## Context

`<Image Source="/Assets/logo.png"/>` and `<Window Icon="/Assets/app.ico"/>` went to Avalonia's runtime
loader as written, and Avalonia read them through its own asset loader — which knows the resources of the
assemblies the process has loaded, and nothing else:

- the environment's `IXamlResourceResolver` was never asked, so a host that resolves `avares://` to the
  files of a project — a designer, which shows what is on disk rather than what was last built — could
  not show an image added since the last build;
- an image the asset loader could not find threw out of the load, and the whole document failed with
  `AXM3002` and `AXM3003`: one missing picture and there was no form at all;
- `XamlMemberDescriptor.ConvertFromText` has no document and no environment, and refused the text, so an
  update that rewrote the source was refused too, and an inspector had nothing to check a path with.

## Decision

**A member whose type is an image or an icon reads its text as the URI of a resource.** The types are the
ones Avalonia's compiler reads an image into — `IImage`, `IImageBrushSource`, `Bitmap` and `WindowIcon`.
A member typed `object` holds text as text, as a button's content does. `XamlMemberDescriptor.ReadsResource`
says which members these are.

**The session reads it, through the environment, against the document.** The attribute is left out of
every projection the session makes — as a handler the class does not answer is — so Avalonia never sees
it. The text is a URI against the document's base; the environment's resolvers find what it names; the
bytes are decoded on the owning thread into a `Bitmap`, or a `WindowIcon` for an icon; and the value is
set on the object its element produced:

- at a load, once the objects exist;
- on an update that rewrites the text, in place, read before the turn like every other value;
- for a part an update rebuilds, read before the turn and set in it, once the map knows the part's objects.

Only literal text is read this way. `{Binding}`, `{StaticResource}` and the rest stay with Avalonia.

**A name that finds nothing is a diagnostic about one attribute.** `AXM2005` when no resolver knows the
URI, and `AXM2014` (`UnreadableResourceValue`, new) when the resource could not be read or does not decode.
At a load it is a warning and the element goes without the value, as an image a running application cannot
find shows nothing. At an update it is an error and the update is refused before anything is written, as
text a member cannot hold is.

**`ConvertFromText` refuses such text, and says why.** It has neither the document the text is relative to
nor the environment that finds it. A host that checks a field as it is typed asks `ReadsResource` and leaves
the check to the update.

**What is read is kept for the environment.** By where the resolver found it and what the bytes are, so a
part rebuilt, or a document loaded again, does not decode the same file twice — and an image whose file has
changed is decoded afresh, because the bytes are read either way and comparing them is what is cheap. One
cache per environment, for the reason the member resolver is per environment.

## Consequences

- A document with an image the last build lacks loads, and shows the image if the environment can find it.
- An image the environment cannot find no longer costs the rest of the document.
- A control populated from its live document inside its constructor (ADR 0014) is still given the text as
  written: a constructor has no turn to read a resource in, and Avalonia's asset loader reads it, as before.
- A design-time value (`d:Source`) is converted as text, and refused for these members as before.
- A file that changes on disk is read again the next time its attribute is loaded, rewritten or rebuilt; a
  source update for the file alone does not re-read it.

## Amendment — what the session cannot reach, find or set

Date: 2026-10-12

A review of this decision found three ways it lost an image Avalonia's loader had shown before it:

- **Reach.** The session sets a value on the object its element produced, and the object map pairs what the
  logical tree, the resources and the styles reach. A brush written in a property element and a template's
  content are reached by none of them, so the value read for them had no object to go to, and the image
  vanished without a word. Such an attribute now stays in the text, for Avalonia's loader, as before this
  decision: an element inside a property element, or inside a type that defers its content
  (`[TemplateContent]` — `ControlTemplate`, `DataTemplate`, `ItemsPanelTemplate` and a library's own), keeps
  its image as written. A template's own attributes are still the session's.
- **Find.** The environment's resolvers know what a host gives them — a designer's projects — and an image
  built into a package's or a library's assembly was `AXM2005`, though Avalonia's asset loader had always
  found it. When no resolver knows an `avares:` URI, Avalonia's asset loader is asked last, as an include the
  environment cannot find is left to Avalonia (ADR 0005).
- **Set.** A setter that refuses an image is a warning about its attribute, but a CLR property is written by
  reflection, which wraps what the setter threw, and that wrapper escaped the load. A write now stands on the
  answers an update does (`XamlObjectReplacement`), and says what the setter itself threw — for a design value
  (`d:`) too, which had the same gap.
