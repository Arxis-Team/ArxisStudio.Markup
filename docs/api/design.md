# Showing a root that cannot be shown

`ArxisStudio.Markup.Xaml.Design` exists for one problem, and it is the problem every designer meets
first.

The commonest document in any Avalonia application is a window. A `Window` is a `TopLevel`, and
Avalonia parents one at construction — so the object the loader correctly produces for
`MainWindow.axaml` is an object nothing can host. Put it in a `ContentControl` and it throws during
layout, off the stack of whatever asked for the form.

```csharp
await using XamlLoadSession session = await XamlLoadSession.CreateAsync(document, environment);

var surface = new XamlDesignSurface();

surface.Attach(session);

canvasItem.Content = surface;      // a Control, hostable anywhere
```

`XamlDesignSurface` is a `Border` that stands in for the root. It is the whole public surface of the
package.

## What it does, and what it deliberately does not

**The root stays the root.** `session.RootObject` is untouched. The object map, `x:Class`, member
resolution and every edit path work exactly as they do without this package — the stand-in is a
presentation, not a substitution. `surface.Root` gives you back the real root, because that is what
edits address.

**It hosts; it does not select.** There is no adorner, no handle, no pointer or keyboard handling
and no inspector here, and there must not be. Selection belongs to whatever draws the design
surface. See [ADR 0012](../adr/0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md).

## Borrowed, not copied

Three things move onto the stand-in while it is attached, and move back on `Detach`:

| | Why it moves |
| --- | --- |
| `Content` | one control cannot be in two logical trees |
| the root's `ResourceDictionary` | Avalonia allows a dictionary exactly one owner |
| the root's `Styles` | the same rule, and a style collection has an owner too |

Moving rather than copying is what keeps merged dictionaries and theme dictionaries intact: they are
the same objects, not a flattened snapshot of whatever entries a copy could reach.

The surface's own resources and styles are not disturbed. The root's dictionary is *merged* into the
surface's rather than replacing it, so a host that put its own resources on the stand-in keeps them
while a form is attached. A key both declare resolves to the host's.

**One surface owns a root at a time.** Attaching a second surface to the same root throws
`InvalidOperationException` rather than half-working: unguarded, the second borrows the substitutes
the first left behind and whichever detaches last empties the window.

While a root is held it reports none of the three. The document is unchanged and still says what it
says, which is what every edit path reads.

## Projected, not snapshotted

Size, theme variant and data context are bound, so an edit through the session shows immediately —
without rebuilding anything, and so without losing focus or scroll position inside the form.

The data context is the one that is found last and looks like something else entirely. A form's
design-time data is set on its root, because `Design.DataContext` is a property of the window; take
the content out and the bindings under it go blank and the form measures to nothing. Worse, whatever
the *host* is bound to arrives in its place. The scope holding the content therefore always has a
data context of its own — the root's for a top-level, an explicit null otherwise.

`Background` is not bound straight through. A window always ends up with one, because the
application the designer itself runs under supplies a themed default, and painting that would show
every undecided form in the tool's own colour while claiming it was the form's. Only a value at
local priority is the form's. [One transition cannot be observed](../limitations.md); the rest is
exact.

## Chrome is data

`Title`, `Icon`, `CanResize` and `Decorations` are properties of a window *as a window*, and there
is no title bar on a stand-in to project them onto. They are published as data and a host draws its
own chrome from them.

`WindowState` is deliberately absent: a window that is never shown is always in its normal state, so
surfacing it would be a promise with nothing behind it.

## Roots that are not windows

`Attach` takes any session. A root that was hostable as it stood is simply held — `IsTopLevel` is
false, the chrome properties say nothing, and nothing is projected, because a root really in the
tree already carries its own resources, styles and variant. The data-context insulation still
applies.

A document that produces no control at all — `App.axaml` produces an `Application`, a resource
dictionary produces a dictionary — sets `HasContent` to false. **Check it.** It is the one signal
that a document loaded and yet there is nothing to show, and a host that does not read it draws an
empty card and says the form appeared.

Content that is not a control is not nothing: a string, or a view model the window resolves with its
own `ContentTemplate`, is presented the way the window would have presented it.

## Lifetime

`Attach` replaces whatever was attached, so call it again after an update that rebuilt the root — it
is not a rebuild, the surface is the same control, and a host holding it in a canvas keeps its
place, its size and its selection.

`Attach`, `Detach` and `Dispose` all require the thread that owns the objects. `Detach` asks the
dispatcher rather than the session, because a host may well have disposed the session first — that
is the ordinary teardown order, and a disposed session answers a thread question with
`ObjectDisposedException`.
