# Loading

Turning a document into live Avalonia objects, and keeping track of which is which.

## The environment

Everything outside the document arrives through the environment, and nowhere else. There is no
project system here: nothing reads a `.csproj`, searches a package cache, or guesses where an
assembly lives. You supply what you have.

```csharp
using ArxisStudio.Markup.Xaml.Loader;

XamlLoadEnvironment environment = XamlLoadEnvironment.CreateDefault(
    assemblies: [typeof(MyControls.Badge).Assembly]);
```

`CreateDefault` gives you the loaded-assembly resolver, a reflection type resolver, the Avalonia
resource resolver and the Avalonia dispatcher — enough to load a document that uses standard
controls plus whichever assemblies you name. Build one by hand when you need more:

```csharp
var environment = new XamlLoadEnvironment
{
    SourceProvider = sourceProvider,
    AssemblyResolver = new CompositeAssemblyResolver(
        new ExplicitAssemblyResolver(typeof(MyControls.Badge).Assembly),
        new DirectoryAssemblyResolver(pluginFolder),
        new LoadedAssemblyResolver()),
    TypeResolver = typeResolver,
    ResourceResolver = new CompositeResourceResolver(unsavedEdits, new FileResourceResolver()),
    RootInstanceFactory = rootFactory,
    Dispatcher = dispatcher,
    Services = services,
};
```

| Member | What it decides |
| --- | --- |
| `SourceProvider` | Where a document's text comes from |
| `AssemblyResolver` | Which assembly an `assembly=` clause means |
| `TypeResolver` | Which CLR type an element name means |
| `ResourceResolver` | What a `ResourceInclude` or `StyleInclude` points at |
| `RootInstanceFactory` | How an `x:Class` root is constructed |
| `Dispatcher` | Which thread owns the objects |
| `Services` | Passed through to markup extensions that ask for services |

The resolvers are interfaces. Implement one and the packages will use it — that is the only way
anything external gets in, and it is what lets a host serve unsaved buffers, a plugin folder, or an
in-memory theme without the library knowing.

## A session

```csharp
await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
    document,
    environment,
    new XamlLoadOptions { Mode = XamlLoadMode.Design },
    token);

var root = session.GetRoot<Control>();
```

`CreateAsync` throws when the document produces nothing. When a failure is ordinary — a preview
pane over a file somebody is still typing — ask for the result instead:

```csharp
(XamlLoadSession? session, XamlLoadResult result) =
    await XamlLoadSession.TryCreateAsync(document, environment, options, token);

if (session is null)
{
    Show(result.Diagnostics);

    return;
}
```

`XamlLoadOptions`:

| Option | Meaning |
| --- | --- |
| `Mode` | `Runtime` or `Design` — see [design mode](updates.md#design-mode) |
| `LocalAssembly` | The assembly unqualified `clr-namespace:` references resolve against |
| `UseCompiledBindingsByDefault` | What `{Binding}` means when the document does not say |

A session is disposable, holds the objects it built, and refuses to work after disposal.

## Objects and elements

The map is the point of the whole exercise: given an object, which markup declared it, and given
markup, which object it produced.

```csharp
object? target = session.GetObject(element);
XamlElement? declaration = session.GetElement(control);
Uri? file = session.GetSourceUri(control);
XamlObjectOrigin origin = session.GetOrigin(control);

XamlObjectMap map = session.Objects;
IReadOnlyList<object> everything = map.Objects;
IReadOnlyCollection<XamlElement> mapped = map.MappedElements;
```

`XamlObjectOrigin` says what kind of markup produced an object, which is what stops a template's
output being passed off as a control's own declaration:

| Origin | Meaning |
| --- | --- |
| `Document` | Declared in this document |
| `Resource` | Came from a resource dictionary, possibly an included file |
| `Style` | Came from a style |
| `Template` | Produced by a template at run time |
| `RuntimeGenerated` | Nothing declared it |

An object declared in an included file is attributed to that file rather than to whichever line of
this one sits at the same number — `GetSourceUri` is how you find out which.

## Members

What a name means on a type — the question the syntax layer deliberately cannot answer:

```csharp
XamlMemberDescriptor member = session.GetMember(control, "Width");

member.IsResolved      // the type has such a member at all
member.Kind            // StyledProperty, DirectProperty, AttachedProperty, ClrProperty, Event,
                       // Content, Collection, Unknown
member.ValueType       // what it holds
member.CanWrite        // and whether you may
member.IsReadOnly
member.IsAttached
member.AvaloniaProperty
member.ClrProperty
member.Event
member.AttachedAccessors
```

Attached members work by their written name: `session.GetMember(control, "Grid.Row")`.

To offer a property list, ask what the object has:

```csharp
ImmutableArray<XamlMemberDescriptor> members = session.GetMembers(control);
```

Every registered Avalonia property the type carries, every attached property registered for it —
under its written `Owner.Member` name — and its public CLR properties, ordered by name and without
duplicates. Which of them are worth showing is still yours to decide: a control has upwards of two
hundred settable members, which is a correct answer and a useless panel. What is answered here is
which exist and what each one is.

The answer can grow while your tool runs, and is deliberately not cached as a whole. Avalonia
registers an attached property in the static constructor of the type that declares it, so `Grid.Row`
becomes a member of every control only once something has caused `Grid` to be initialised.

## Values

Before writing a property, ask where its current value came from:

```csharp
XamlValueInfo info = session.GetValueInfo(control, TextBlock.TextProperty);

info.Source                  // Unset, Local, Binding, Style, StyleTrigger, Template, Inherited, Animation
info.HasBinding
info.EffectiveValue          // what the object currently holds
info.SourceValue             // what the document says, as a XamlValue
info.WouldDestroyExpression  // writing a literal here would replace a binding or a resource reference
```

`WouldDestroyExpression` is the one a property inspector must not ignore. Overwriting
`{Binding Customer.Name}` with the text it currently displays is the single most natural way for a
tool to quietly damage a document.

## Writing through the session

```csharp
XamlEditResult result = session.SetValue(control, Layoutable.WidthProperty, 160d);
XamlEditResult expression = session.SetXamlValue(
    control, TextBlock.TextProperty, XamlValue.Parse("{Binding Customer.Name}"));

if (!result.Applied)
{
    Show(result.Diagnostics);
}
```

The member is validated, the value converted, the object updated and the document updated — in that
order, and if writing the document fails the object is put back. The two never end up silently
disagreeing.

Replacing a binding is allowed, because a caller may mean exactly that, but it is reported.

**This writes the session's own document and creates no undo entry.** A tool with a history writes
through the document instead — record the edit on a `XamlDocumentEditor`, apply it through
`XamlWorkspace`, and let [an update](updates.md) bring the objects in line. The two directions must
not be mixed on one document: the session's document is not the workspace's, and this would advance
one while the other stood still.

## Threading

```csharp
session.VerifyAccess();   // throws AXM3004 from the wrong thread
```

Objects belong to the thread that created them. The session marshals through the environment's
dispatcher where it can, and fails clearly where it cannot.
