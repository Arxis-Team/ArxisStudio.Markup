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

## What a document is

A window, a user control, a templated control's look, a set of styles — asked of a document
without loading it:

```csharp
XamlDocumentClassification what =
    await XamlDocumentClassifier.ClassifyAsync(document, environment, token);

switch (what.Kind)
{
    case XamlDocumentKind.TemplatedControl:
        foreach (XamlTemplatedType control in what.TemplatedTypes)
        {
            // control.Type, or null before the project is built; control.Declaration sets it
        }

        break;

    case XamlDocumentKind.Styles or XamlDocumentKind.ResourceDictionary:
        XamlElement? preview = what.Preview;   // inside <Design.PreviewWith>: the one visual it has
        break;
}
```

| Kind | Root |
| --- | --- |
| `Application` | `Application`, or a type derived from it — `App.axaml` |
| `Window` | `Window`, or a type derived from it |
| `UserControl` | `UserControl`, or a type derived from it |
| `Control` | any other control: a panel, a border, a control of your own |
| `TemplatedControl` | a set of styles or a dictionary that sets the `Template` of a control written outside `https://github.com/avaloniaui` |
| `Styles` | `Styles`, `Style` or `ControlTheme` |
| `ResourceDictionary` | `ResourceDictionary` |
| `Other` | anything else — a brush, a data template, an object of your own |
| `Unknown` | a root the environment cannot resolve, or no root at all |

The root element's type decides, and its base types with it: `<local:ToolWindowBase>` is a `Window`
when `ToolWindowBase` derives from one. `RootType` is that element's type, not the `x:Class`.
`IsCustomRoot` says the root is written outside Avalonia's namespace — `<local:ToolWindowBase>`,
`<local:Badge>` — which is how a control of your own is told from Avalonia's `Border`, whatever its
kind.

A templated control's look has a set of styles or a dictionary at its root, and only what it sets
says what it is for. Setting `Template` on Avalonia's `Button` is a theme, and the file stays
`Styles`; setting it on a control written in a namespace of your own — `controls|Badge`,
`TargetType="controls:Badge"` — makes it a `TemplatedControl`. So does a control theme for such a
control that is `BasedOn` a theme in the same file that sets the template, which is how a theme
library writes one template for several controls; a base theme from another file supplies a
template this file does not, and is not followed. A template written inside `Design.PreviewWith`
does not count.

Whose control it is, the namespace the name is written in decides, so the answer holds before the
project is built: when the environment does not have the control, the kind is the same,
`IsResolved` is `false`, the control's `Type` is `null`, and a warning sits on its name. When the
environment has it, it must derive from `TemplatedControl`.

A root the environment cannot resolve is `Unknown`, with an error on the root's name. It is not
guessed at: `ToolWindowBase` says nothing reliable about what it derives from, and a document whose
root does not resolve cannot be loaded either.

Classifying creates nothing and needs no Avalonia thread. It goes through the same cached resolver
a session does, so classifying a folder costs a lookup per distinct type.

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
| `LocalAssembly` | The assembly unqualified `clr-namespace:` references resolve against, and whose non-public members — a private handler — the document may name; by default the resolved `x:Class`'s |
| `UseCompiledBindingsByDefault` | What `{Binding}` means when the document does not say |
| `RootAccess` | A host that borrows parts of the root, lending them back for every write — see [updates](updates.md#a-host-that-borrows-the-root) |
| `ClassUse` | `Construct` the class `x:Class` names (the default), or build the root `AsWritten` — see [a class left out on request](#a-class-left-out-on-request) |

A session is disposable, holds the objects it built, and refuses to work after disposal.

### An `x:Class` root

A document naming a class is loaded by creating the class and populating the instance. Every class
a project writes calls `InitializeComponent()` from its constructor, which loads markup too — so the
session lends that load its document: the class's constructor runs, its generated
`InitializeComponent` populates the instance from the session's projection, in the session's mode,
and its own code after the call sees the controls that are shown. One population, never two
(ADR 0015). A root with `<Window.Resources>` used to fail here with "An item with the same key has
already been added", and its styles and handlers came out doubled.

The hook the session borrows is the one `XamlLivePopulation` stands on, and it is borrowed rather
than taken: a document registered for the same type keeps populating every *placed* copy of it.
`RootInstanceFactory` decides how the instance is constructed; an instance it hands over without
constructing it here is populated after the fact, as it always was.

### A class there is not

A form the project has not built yet names a class no assembly in the environment has, and a
class can also not be what the root says it is. Neither stops the load: the class is reported,
and the document is loaded without it — the root is built as the element it is written as.

```csharp
// <UserControl x:Class="Contoso.Views.NotBuiltYet"> … with a Button Click="SaveClicked" inside
(XamlLoadSession? session, XamlLoadResult result) =
    await XamlLoadSession.TryCreateAsync(document, environment, options);

session!.RootObject;   // a UserControl, not a NotBuiltYet
// AXM3020 (warning): x:Class names 'Contoso.Views.NotBuiltYet', which was not found …
// AXM3005 (warning): 'Click' names the handler 'SaveClicked', but the document has no x:Class …
```

`UnresolvedRootType` (AXM3020) is a warning, because a class nobody has built yet is the
environment lagging behind the document; `IncompatibleRootType` (AXM3021) stays an error, because
that one is the document contradicting itself. Names still resolve — `x:Name` needs a name scope,
not a class — and handlers are reported and left out, because a handler names a method of the
class and there is no class to find it on. The document keeps every one of them.

What the load left out of the text Avalonia was given, every later projection of the session leaves
out too, so the session stays updatable: a child added to the root rebuilds its content, and a panel
holding a button with a handler is rebuilt without the handler. A class that resolves later — after a
build — is a new environment and a new session; a changed `x:Class` is one anyway.

### A class left out on request

The same road is open to a host that does not want the class constructed at all. A designer showing a
program's forms wants what the program's `App.axaml` declares — its theme and its resources — and not
the program's `App`, whose constructor is the program starting up (ADR 0027):

```csharp
var options = new XamlLoadOptions { Mode = XamlLoadMode.Design, ClassUse = XamlClassUse.AsWritten };

await using XamlLoadSession session = await XamlLoadSession.CreateAsync(appDocument, environment, options);

var application = session.GetRoot<Application>();   // an Application, never the program's App
```

Nothing is reported about the class — nothing is wrong with it — and a handler the document names is
reported and left out, with nothing to be hooked up to. The styles and the dictionary have one owner
each, so a host lending them to a form takes them over rather than sharing them, and loads the document
once per form:

```csharp
IStyle[] styles = [.. application.Styles];
IResourceDictionary resources = application.Resources;

application.Styles.Clear();
application.Resources = new ResourceDictionary();

form.Styles.AddRange(styles);
form.Resources.MergedDictionaries.Add(resources);
```

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

Avalonia's property system in the three shapes it has, plus the CLR properties around it: styled
and direct properties of the type and its bases, attached properties registered for it — under the
`Owner.Member` name a document writes them with — and public CLR properties. Ordered by name and
without duplicates: a property registered both ways, as `KeyboardNavigation.IsTabStop` is, appears
once under its simple name.

Which of them are worth showing is still yours to decide: a control has upwards of two hundred
settable members, which is a correct answer and a useless panel. What is answered here is which
exist and what each one is.

The answer can grow while your tool runs, and is deliberately not cached as a whole. Avalonia
registers an attached property in the static constructor of the type that declares it, so `Grid.Row`
becomes a member of every control only once something has caused `Grid` to be initialised.

What *is* cached — the descriptors themselves — belongs to the environment:

```csharp
XamlMemberResolver members = environment.MemberResolver;   // one per environment by default

// Sharing the cache between environments, when they resolve the same assemblies:
var shared = new XamlMemberResolver();

var environment = new XamlLoadEnvironment
{
    SourceProvider = provider,
    AssemblyResolver = assemblyResolver,
    TypeResolver = typeResolver,
    ResourceResolver = resourceResolver,
    MemberResolver = shared,
};
```

That matters for a tool that rebuilds the user's control library and loads it again: build a new
environment for the new assemblies and what was known about the old ones goes with the old one. A
process-wide cache would hold those types alive against a collectible load context and go on
answering about a build that no longer exists. `XamlMemberResolver.Instance` remains for a caller
with no environment at all.

### Where do unnamed children go?

```csharp
XamlMemberDescriptor? content = members.FindContent(typeof(Border));   // Child
```

Avalonia says which member that is with `[Content]`, and every control library says it for its own
controls: `Panel.Children`, `ContentControl.Content`, `Decorator.Child`, `ItemsControl.Items`,
`TextBlock.Inlines`, `YourHost.Slots`. A designer deciding whether a control can take a dropped
child, and where, asks this rather than testing for the framework's own base classes — which is
also how the loader finds content when an update has to replace or reorder it.

`null` means the type declares none. A few types take children another way — `Style`,
`ControlTheme` and their like implement `IAddChild` — and those answer `null` here.

### Is this text a value?

```csharp
XamlValueConversionResult converted = member.ConvertFromText("6,0,4,0");

if (!converted.Succeeded)
{
    ShowError(converted.Error);   // "'…' could not be read as Thickness: …"
}
```

The conversion an update performs, asked in advance and writing nothing — which is how a property
field says that what has been typed so far is not a value yet, without creating an undo entry and
watching the update roll it back. The member's `TypeConverter` first, then the public static
`Parse` that Avalonia types such as `Thickness` and `CornerRadius` are read by instead.

Markup extensions are not values of this kind: `{Binding Customer.Name}` is resolved by a load, so
check for one with `XamlValue.Parse` before asking.

## What a document can name

A toolbox and a data panel ask the same question of different assemblies: which types a document can
name, in which namespace, and what it can do with each.

```csharp
XamlTypeCatalog catalog = XamlTypeCatalog.Create([typeof(Button).Assembly, projectAssembly]);

foreach (XamlTypeEntry entry in catalog.Entries.Where(static e =>
    e.Kinds.HasFlag(XamlTypeKinds.Control | XamlTypeKinds.Creatable) && !e.Kinds.HasFlag(XamlTypeKinds.TopLevel)))
{
    // entry.Name, entry.XmlNamespace, entry.SuggestedPrefix, entry.FullName, entry.AssemblyName
}
```

An entry's namespace is the first one its assembly maps the type's CLR namespace to with
`XmlnsDefinition` — read the way `XamlTypeResolver` reads it when it resolves a document — and
`using:` with the CLR namespace where the assembly maps none. `SuggestedPrefix` is what the library's
`XmlnsPrefix` says for that namespace; pass it to `XamlDocumentEditor.Qualify` as the preferred prefix
and the editor declares the namespace where the document lacks it.

`Kinds` answers what a tool asks before offering a type: `Creatable` (a document can write it as an
element), `Control` and its shapes — `Panel`, `ContentControl`, `Decorator`, `ItemsControl`,
`TemplatedControl`, `UserControl`, `TopLevel` — `CompiledMarkup` for an `x:Class` whose markup was
compiled into it, and `Data` for a type that is not an Avalonia object at all.

**Nothing in a catalog holds a type or an assembly.** A toolbox filled from one generation of a
project's code stays on screen while the next one loads, and a `Type` in it would keep the previous
generation in memory. A catalog is a reading rather than a view, so a build that adds a control is a
new catalog. `Diagnostics` names an assembly some of whose types could not be read (`AXM2013`).

## What a binding reads

What the bindings written on an element read from:

```csharp
XamlDataContextInfo data = await session.GetDataContextAsync(element, token);

data.DataTypeElement          // where the x:DataType in scope is written, or null
data.WrittenDataType          // "vm:MainViewModel" or "{x:Type vm:MainViewModel}", as written
data.DataType                 // the type it names, resolved through the environment
data.DesignDataContextType    // the type of the design data the element's object shows
data.CompilesBindings         // the nearest x:CompileBindings, or the session's default
```

The data type is what compiled bindings are checked against and what a panel offers members of; the
design data — `Design.DataContext`, or what the class sets in its constructor — is what a binding
without a data type is read against in the preview. The element must be one of `session.Document`.

What a binding can name on a source, and whether a path resolves:

```csharp
ImmutableArray<XamlBindableMember> members = environment.MemberResolver.EnumerateBindable(data.DataType!);
// Name, TypeName ("string", "ObservableCollection<Customer>"), CanWrite, IsCollection, IsCommand

XamlBindingPathResult path = environment.MemberResolver.ResolveBindingPath(data.DataType!, "Customer.Name");

switch (path.Status)
{
    case XamlBindingPathStatus.Resolved: /* path.ResultType */ break;
    case XamlBindingPathStatus.Broken: /* path.Step names nothing; path.Message says so */ break;
    case XamlBindingPathStatus.NotUnderstood: /* not checked: say so, not "broken" */ break;
}
```

Public instance properties with a public getter and no index are what a binding reads, each listed
once: a property a derived type hides is listed as the derived type declares it, and an interface's
list includes what the interfaces it extends declare. Paths are read as a binding reads them as far as
types go — dotted members, integer and string indexers, a leading `!` — and anything further is
`NotUnderstood`, never `Broken`: a step typed `object`, an attached property in parentheses, a cast,
`$parent`, `#name`.

The member list is names. The `Type` answers — `DataType`, `DesignDataContextType`, `ResultType` —
belong to whatever generation of code the session was built in: read what is needed from them and let
them go. ADR 0026 records the rule.

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

Replacing a binding is allowed, because a caller may mean exactly that, but it is reported —
and the binding ends with it. The object stops following its source, so it goes on agreeing with
the document, which now holds the literal.

The attribute is written under the name the document would use for it. A property of the object's
own type is written as it is named, `Width`; an attached property is written `Owner.Member` —
`Grid.Row`, or `controls:Dock.Side` with the owner's namespace declared on the root when nothing in
scope binds it — and an attribute already there under any prefix is the one that is changed, and read
back. The map keeps every object it knew, including the ones an earlier update rebuilt.

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
