# ArxisStudio.Markup — API guide

Three libraries for reading, editing and running Avalonia XAML without compiling it.

They exist to make one thing possible: a tool that shows a XAML document and the live objects it
describes at the same time, lets a user change either, and never damages the file doing it. A form
designer is the obvious such tool, but so is a hot-reload host, a linter, a refactoring command, or
a migration script.

Written against **v0.1.0-preview**. Every example here uses only published API.

## The guides

| Guide | What it covers |
| --- | --- |
| [Documents](documents.md) | Text, spans, parsing, round-trip, navigating a syntax tree, element paths, diagnostics |
| [Editing](editing.md) | Changing attributes and elements without disturbing anything else |
| [Workspace and history](workspace.md) | Several open documents, transactions, undo and redo |
| [Loading](loading.md) | Environments, resolvers, sessions, objects ↔ elements, enumerating members, values |
| [Updates](updates.md) | Bringing running objects in line with a changed document, design mode, includes |

[Known limitations](../limitations.md) is the honest list of what these packages do not do. Read it
before promising anything to your users.

## Which package

```text
ArxisStudio.Markup              text, documents, versions, transactions, undo — no XAML, no Avalonia
        ↑
ArxisStudio.Markup.Xaml         lossless XAML syntax, editing, resource graph, workspace — no Avalonia
        ↑
ArxisStudio.Markup.Xaml.Loader  live Avalonia objects, resolution, sessions, updates
```

Reference the highest one you need; it brings the others with it. A tool that only reads and edits
markup — a formatter, a linter, a codemod — needs `ArxisStudio.Markup.Xaml` and never loads
Avalonia at all.

```xml
<PackageReference Include="ArxisStudio.Markup.Xaml.Loader" Version="0.1.0-preview" />
```

## Three rules everything here follows

**The document is the source of truth.** Objects are built from text; text is never generated from
objects. `Text="{Binding Customer.Name}"` is never written back as `Text="Alice"` because that is
what it currently evaluates to.

**An unchanged document round-trips byte for byte,** and a single edit leaves comments, blank
lines, indentation, attribute order, prefixes and quote style exactly as they were. This is not
best-effort tidiness: an edit *is* a set of text changes over the original snapshot, so unrelated
characters are not part of it and cannot move.

**Unknown content survives.** An element, attribute, namespace, directive or markup extension that
nothing here recognises may raise a diagnostic, but is never discarded or rewritten.

## Ten minutes end to end

Read a file, look at it, change one property, run it, and save.

```csharp
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;

// 1. Parse. The URI is how includes and diagnostics refer to this document.
var document = XamlDocument.Parse(
    await File.ReadAllTextAsync(path),
    new XamlParseOptions { DocumentUri = new Uri(path) });

// 2. Find something. Elements point back into the text they came from.
XamlElement button = document
    .DescendantElements()
    .Single(element => element.GetDirective(XamlDirectives.Name) == "Save");

// 3. Edit the document, not an object.
XamlDocument edited = document.SetAttribute(
    button, XamlQualifiedName.Parse("Width"), "160");

// 4. Build the objects the document describes.
XamlLoadEnvironment environment = XamlLoadEnvironment.CreateDefault();

await using XamlLoadSession session = await XamlLoadSession.CreateAsync(edited, environment);

var root = session.GetRoot<Control>();

// 5. Save. Writing an unchanged document back is a copy; this one differs by three characters.
await File.WriteAllTextAsync(path, session.Document.GetText());
```

Nothing was compiled, and no application was launched.

A tool that keeps state about an element — which one is selected, which nodes are expanded — holds a
`XamlElementPath` rather than the element itself, because an edit replaces every element in the
document. See [Referring to an element after an edit](documents.md#referring-to-an-element-after-an-edit).

## Errors

Ordinary user errors — bad syntax, an unresolved type, a missing resource — are
`MarkupDiagnostic` values with a stable code and a source span, never exceptions. Codes are grouped
by what noticed the problem:

| Range | Raised by |
| --- | --- |
| `AXM1xxx` | Syntax: the lexer and parser |
| `AXM2xxx` | Resolution: assemblies, types, resources, includes |
| `AXM3xxx` | Loading and synchronisation: building objects, applying updates |

Exceptions are reserved for invalid API use, disposed sessions, broken invariants, cancellation and
unrecoverable failures. Where an operation can fail for an ordinary reason, it returns a result
instead of throwing: `XamlLoadSession.TryCreateAsync`, `XamlUpdateResult`, `XamlEditResult`.

## Threading

Parsing, editing and everything in the two lower packages are free of thread affinity: do them
wherever you like.

Creating and mutating Avalonia objects is not. A session checks, and calling from the wrong thread
fails with `AXM3004` rather than corrupting state that would surface later and somewhere else. Give
the environment a dispatcher — `AvaloniaXamlDispatcher` by default — and the session will marshal
for you.

One session also mutates one thing at a time: the asynchronous updates queue behind each other,
and the synchronous edits refuse rather than wait. See
[Updates](updates.md#one-session-mutates-at-a-time) for why the two differ.

Every asynchronous method takes a `CancellationToken`.
