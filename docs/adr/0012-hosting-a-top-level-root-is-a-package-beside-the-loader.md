# 12. Hosting a `TopLevel` root is a package beside the loader, not a part of it

Date: 2026-08-07
Status: Accepted

## Context

A designer built on these libraries — `ArxisStudio.ProjectSystem/samples/FormsDesigner` is the
worked example — opens a project's `.axaml` files and shows them live. The most common one in any
Avalonia application is `MainWindow.axaml`, whose root is a `Window`.

A `Window` is a `TopLevel`, and Avalonia parents it to a `TopLevelHost` at construction. Making it
the content of anything throws during layout: `InvalidOperationException: … already has a visual
parent TopLevelHost`. So the object this loader correctly produces for the commonest document in the
ecosystem is an object that cannot be displayed.

Nothing here answers that today, and nothing in `ArxisStudio.DesignEditor` does either — its README
describes the loaded-form case and ends "its markup root is an ordinary Avalonia panel", and its
worked example would throw on every `MainWindow.axaml` in existence.

So each host invents the answer. FormsDesigner's is to keep the object the document produced apart
from the object the canvas shows: detach `Window.Content`, host that, and draw the window's chrome
itself. It works, and it is wrong in four ways that a host discovers one at a time:

- `<Window.Styles>` and `<Window.Resources>` stop applying, because style and resource lookup walk
  the logical tree and the content now has a different parent. A form that declares its styles at the
  root — a very common shape — renders unstyled.
- The window's `Background` is not shown; the host paints its own.
- `RequestedThemeVariant` is a `TopLevel` property, so a form that asks for a variant does not get it.
- Inherited properties — `FontFamily`, `FontSize`, `Foreground` — stop at the new parent.

And one more that is found last and looks like something else entirely: detaching the content
detaches it from the window's `DataContext`, and `Design.DataContext` is set on the root, so every
binding underneath goes blank and the form measures to nothing. That looks exactly like a form that
failed to load.

This is not a defect in what the loader produces. It is a missing layer between "the object the
document describes" and "something a design surface can host".

## Decision

**The layer belongs in this family, as a fourth package beside the loader, not inside it.**

### In this family, because that is where its dependencies are

What it needs is a `XamlLoadSession` and Avalonia. What it builds is an ordinary `Control` — any
host that can show a `Control` can show it, including `DesignEditorItem`. It needs nothing from
`ArxisStudio.DesignEditor`.

Putting it there instead would make an Avalonia control library depend on a document model for the
sake of one scenario, and would break that library's own discipline, which is that it reads a visual
tree and knows nothing about where the tree came from.

### Beside the loader, because it is not a load result

`ArxisStudio.Markup.Xaml.Loader` promises one thing: the object the document describes, and the
correspondence between that object and the text. A surrogate is an object the document does not
describe. Publishing it from the session would put two answers behind one question — `RootObject`
and something beside it, only one of which is what was loaded.

There is also a versioning argument. The rules this layer encodes are Avalonia's visual-tree rules,
and they move: `ExtendClientAreaChromeHints` was removed and replaced by `Window.WindowDecorations`
inside one major version. The loader's contract should not version with them.

Every mature stack that has met this has drawn the line in the same place. Avalonia keeps
`Avalonia.DesignerSupport` out of `Avalonia.Markup.Xaml.Loader`; WPF separates
`System.Windows.Markup` from the designer's hosting; WinForms separates serialization from
`System.ComponentModel.Design`.

### What the package is

`ArxisStudio.Markup.Xaml.Design`. Given a session whose root is a `TopLevel`, it produces the
`Control` that stands in for it — carrying the root's `Resources`, `Styles`, `DataContext`, requested
theme variant, background and declared size, with the root's content inside — and rebuilds it when
an update rebuilt the root.

`RootObject` stays the real `Window`, so the object map, `x:Class`, the member resolver and every
edit path work exactly as they do now. The document is still the truth; only the presentation gets a
stand-in.

### A projection, not a snapshot

The question that settles the shape is: what happens when the inspector edits a property of the
window itself?

The edit needs nothing new. The map has the root, so `GetMembers`, `GetValueInfo` and `SetValue`
all take the `Window` and the write lands in the document; none of them knows a surrogate exists.
What is missing is the other direction — the edit does not show, because what is on screen is the
stand-in and what was painted is the window.

So the surrogate mirrors the root rather than copying it once. `Resources` is a settable property
and is shared by reference, so a resource added to the window appears in the surrogate's lookup
immediately; `Styles` is itself an `IStyle` and is nested by reference for the same reason; the
scalar properties — `Background`, `Width`, `Height`, `RequestedThemeVariant` and the inherited text
properties — are bound. An edit then shows without rebuilding anything, which also means without
losing focus or scroll position inside the form.

One category cannot be mirrored, because the surrogate has no title bar to mirror it onto: `Title`,
`Icon`, `WindowDecorations`, `CanResize`, `WindowState` are properties of the window *as a window*.
The surrogate publishes them as data — this root is a `TopLevel` and declares these — and the host
draws chrome from them. That is still hosting; it is not selecting, and the boundary below is
unaffected.

### One writer for size

`Width` and `Height` acquire two candidate writers: the projection from the document, and the
host's resize gesture. Only one of them may write, and it is the document. A resize edits the
document through the session and the projection picks the new value up; the surrogate never writes
back. The alternative is a feedback loop in which the form shivers on every frame of a drag, which
is a classic failure of this kind of tool and is much easier to avoid than to debug.

### The loader does not change

Worth stating, because it is what makes the split cheap. A host already re-reads the session after
every update — an update that reaches far enough rebuilds the root, and hosts handle that today — so
the information needed to keep a surrogate in step is already published. Nothing in
`XamlLoadSession`, `XamlLoadOptions` or `XamlUpdateResult` needs to grow.

### Where this sits against the hard boundaries

`CLAUDE.md` forbids adding "a visual designer, selection adorners, property-inspector UI, drag and
drop, pointer/keyboard interception" to `src/`, and that rule is the first thing a reader will raise
against this package. It stands, and this is not an exception to it.

The distinction is that this layer makes a loaded object **viewable**; it does not make it
**editable by gesture**. It has no notion of selection, no adorner, no handle, no pointer or keyboard
handler, and no inspector. Those remain out of scope and remain the host's, exactly as
[ADR 0006](0006-inspector-in-the-sample.md) settled for the property inspector.

The test to apply if the package ever grows: if it needs to know what is selected, the boundary was
drawn in the wrong place and the new code belongs to a host or an editor, not here.

## Consequences

- A fourth package exists in `src/`, and the hard-boundary rule in `CLAUDE.md` names it and says what
  it may not contain. Without that, the next contributor reads "never add a visual designer" and
  deletes it.
- A host that shows `Window`-rooted documents stops writing its own detach-and-transplant, and stops
  discovering the `DataContext` consequence the hard way. FormsDesigner's `FormViewModel` becomes a
  caller instead of an implementation, and its size handling changes: it seeds the card from
  `root.Width` once today and then lets the canvas own it, which drifts from the document silently.
- Forms are shown as they are written: their own styles, resources, background, theme variant and
  inherited text properties reach the surface. That is the whole point — a designer that shows
  something other than what the document says is worse than no designer.
- `ArxisStudio.DesignEditor` keeps knowing nothing about documents, and this family keeps knowing
  nothing about selection. Neither gains a dependency on the other.
- The loader's public surface is unchanged, so this can be built without touching a contract anything
  already depends on.
- Nothing is implemented yet. This ADR records where the work goes and why the obvious alternative —
  a second root on the session — was rejected, so that neither is re-argued from scratch.
