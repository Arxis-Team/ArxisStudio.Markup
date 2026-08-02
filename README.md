# ArxisStudio.Markup

`ArxisStudio.Markup` is a set of .NET libraries for lossless XAML document editing, Avalonia object loading, and controlled round-trip synchronization between source XAML and live Avalonia objects.

The project is intended to provide infrastructure for tools that need to:

- open existing Avalonia `.axaml` files;
- preserve the original source structure and formatting;
- load real Avalonia controls, including custom controls;
- inspect and modify Avalonia properties;
- preserve bindings, resources, styles, templates, events, and `x:Class`;
- update runtime objects after source changes;
- write the resulting document back to valid XAML without destroying unrelated source text.

The current development scope is limited to the markup libraries described in this document. This repository does **not** implement a visual designer, IDE, project system, NuGet package manager, or MSBuild integration.

## Status

Milestones 0 to 14 are implemented, and every item under *Definition of done for the first preview release* holds. The state at the end of Milestone 11 is tagged `v0.1.0-preview`; milestones 12 to 14 came after it. This document stays the contract: the milestones below are the plan, not a record of what happened.

Documentation for people building on these packages lives in [`docs/api/`](docs/api/README.md), and what the packages deliberately do not do is in [`docs/limitations.md`](docs/limitations.md).

The initial package family consists of:

```text
ArxisStudio.Markup
ArxisStudio.Markup.Xaml
ArxisStudio.Markup.Xaml.Loader
```

The dependency direction must remain:

```text
ArxisStudio.Markup
        ↑
ArxisStudio.Markup.Xaml
        ↑
ArxisStudio.Markup.Xaml.Loader
```

Circular dependencies are not allowed.

## Terminology

This project uses the term **XAML** in assembly names, namespaces, and public API names, following Avalonia's own terminology:

- `Avalonia.Markup.Xaml`;
- `Avalonia.Markup.Xaml.Loader`;
- `AvaloniaXamlLoader`;
- `AvaloniaRuntimeXamlLoader`;
- `RuntimeXamlLoaderDocument`.

The `.axaml` extension identifies Avalonia XAML files, but public types in this project should use `Xaml`, not `Axaml`.

Examples:

```csharp
XamlDocument
XamlElement
XamlSyntaxTree
XamlLoadSession
```

Do not use names such as:

```csharp
AxamlDocument
AXAMLDocument
AXamlLoader
```

The compatibility target is Avalonia XAML. The project does not currently promise semantic compatibility with WPF, WinUI, MAUI, or other XAML dialects.

## Core design principles

### 1. The source document is the source of truth

The project is not a generic serializer that reconstructs XAML from an arbitrary runtime object tree.

Reconstructing a document only from live objects would lose information such as:

```xml
Text="{Binding Customer.Name}"
Background="{DynamicResource SurfaceBrush}"
Theme="{StaticResource PrimaryButtonTheme}"
Click="SaveClicked"
```

At runtime, these expressions may have already produced ordinary CLR values. Serializing those values would incorrectly replace the original expressions.

The library must instead maintain a persistent relationship between:

- the original XAML syntax tree;
- XAML members and values;
- the objects created from those nodes;
- explicitly committed edits.

All supported edits should update the document model and runtime object in one controlled operation.

### 2. Round-trip preservation is a primary requirement

Loading and saving an unchanged document must produce byte-for-byte identical text whenever encoding and byte-order-mark handling permit it.

When one value is changed, unrelated source text must remain unchanged, including:

- comments;
- blank lines;
- indentation;
- attribute order;
- namespace prefixes;
- quote style;
- whitespace around `=`;
- markup-extension formatting;
- unknown attributes;
- unknown elements;
- future XAML directives not yet understood by the library.

### 3. Syntax and runtime semantics must remain separated

`ArxisStudio.Markup.Xaml` understands XAML syntax and document structure. It must not require Avalonia runtime assemblies.

`ArxisStudio.Markup.Xaml.Loader` understands Avalonia types, properties, resources, styles, templates, bindings, and runtime object creation.

### 4. Unknown content must survive

The parser must be forward-compatible. An unknown element, attribute, namespace, directive, or markup extension is not a reason to discard or rewrite source text.

Unknown content may produce a diagnostic, but it must remain available for round-trip serialization.

### 5. External environments must be supplied through abstractions

The current libraries do not inspect `.sln`, `.csproj`, or NuGet metadata.

Custom assemblies, source files, and resources must be supplied through interfaces such as:

```csharp
IXamlSourceProvider
IXamlAssemblyResolver
IXamlTypeResolver
IXamlResourceResolver
IXamlRootInstanceFactory
```

A future project-system package will implement those interfaces without requiring breaking changes in the markup libraries.

### 6. No dependency on Avalonia or XamlX internals

Use public Avalonia APIs wherever possible, including `AvaloniaRuntimeXamlLoader`.

Do not copy, fork, or directly depend on internal Avalonia/XamlX compiler implementation details unless a separate architectural decision explicitly approves it.

### 7. Loading user XAML is executable behavior

Creating XAML objects can execute:

- constructors;
- property setters;
- markup extensions;
- type converters;
- bindings;
- event hookup;
- resource factories;
- custom control code.

The loader API and documentation must clearly state this. The library does not provide a security sandbox.

## Repository layout

Use the following initial structure:

```text
ArxisStudio.Markup/
├── ArxisStudio.Markup.sln
├── Directory.Build.props
├── Directory.Packages.props
├── README.md
├── src/
│   ├── ArxisStudio.Markup/
│   │   └── ArxisStudio.Markup.csproj
│   ├── ArxisStudio.Markup.Xaml/
│   │   └── ArxisStudio.Markup.Xaml.csproj
│   └── ArxisStudio.Markup.Xaml.Loader/
│       └── ArxisStudio.Markup.Xaml.Loader.csproj
├── tests/
│   ├── ArxisStudio.Markup.Tests/
│   ├── ArxisStudio.Markup.Xaml.Tests/
│   └── ArxisStudio.Markup.Xaml.Loader.Tests/
├── benchmarks/
│   └── ArxisStudio.Markup.Benchmarks/
└── samples/
    └── ArxisStudio.Markup.Xaml.Loader.Sample/
```

The sample must demonstrate library usage only. It must not become a visual designer.

Use central package management. Enable nullable reference types and treat compiler warnings as errors.

The initial target framework should be `net8.0` unless existing repository constraints require another target. Avalonia and test-package versions must be centralized in `Directory.Packages.props`.

## Package 1: ArxisStudio.Markup

`ArxisStudio.Markup` provides format-independent document infrastructure.

It must not depend on:

- Avalonia;
- XML-specific types in its public model;
- MSBuild;
- NuGet;
- a project-system implementation;
- UI frameworks.

### Responsibilities

#### Source text

Provide an immutable or snapshot-based source-text abstraction:

```csharp
public abstract class SourceText
{
    public abstract int Length { get; }
    public abstract char this[int index] { get; }
    public abstract string GetText(TextSpan span);
    public abstract SourceText WithChanges(
        IReadOnlyList<TextChange> changes);
}
```

Required supporting types:

```csharp
public readonly record struct TextSpan(int Start, int Length);
public readonly record struct TextPosition(int Line, int Column);
public readonly record struct TextChange(TextSpan Span, string NewText);
public sealed class TextLine;
public sealed class TextLineCollection;
```

Requirements:

- efficient line and column lookup;
- immutable snapshots;
- version tracking;
- ordered non-overlapping text changes;
- preservation of newline style;
- cancellation support for expensive operations;
- no hidden dependency on filesystem paths.

#### Document identity and versioning

Provide stable document identity:

```csharp
public readonly record struct MarkupDocumentId(Guid Value);

public readonly record struct DocumentVersion(long Value)
{
    public DocumentVersion Next() => new(Value + 1);
}
```

Document identity must not change merely because a new text snapshot is created.

#### Document sources

Define a provider abstraction:

```csharp
public interface IMarkupSourceProvider
{
    ValueTask<MarkupSource?> TryGetSourceAsync(
        Uri uri,
        CancellationToken cancellationToken);
}
```

Initial implementations:

- `InMemoryMarkupSourceProvider`;
- `FileMarkupSourceProvider`;
- `CompositeMarkupSourceProvider`;
- `StreamMarkupSource`.

The composite provider must honor explicit ordering. An in-memory unsaved document should be able to override a file with the same URI.

#### Diagnostics

Provide a common diagnostic representation:

```csharp
public enum MarkupDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record MarkupDiagnostic(
    string Code,
    string Message,
    MarkupDiagnosticSeverity Severity,
    Uri? DocumentUri = null,
    TextSpan? Span = null);
```

Diagnostics must:

- have stable machine-readable codes;
- optionally point to a source span;
- optionally include related locations;
- never require parsing exception messages;
- distinguish parse, resolution, load, and synchronization failures.

#### Workspace

Provide a workspace that owns open documents and their current snapshots:

```csharp
public sealed class MarkupWorkspace
{
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    public event EventHandler<DiagnosticsChangedEventArgs>? DiagnosticsChanged;

    public MarkupTransaction BeginTransaction(string description);
}
```

The workspace must support:

- opening documents from providers;
- in-memory document updates;
- stable versions;
- document replacement;
- closing documents;
- document change notifications;
- batching notifications during transactions;
- concurrent reads of immutable snapshots.

#### Transactions and undo/redo

Changes to one or more documents must be representable as one transaction:

```csharp
using var transaction =
    workspace.BeginTransaction("Rename resource");

// Apply changes to one or more documents.

transaction.Commit();
```

Required behavior:

- commit;
- rollback;
- undo;
- redo;
- grouping multiple text/document changes;
- preventing partial commits after failure;
- preserving document versions;
- exposing human-readable descriptions.

#### Dependency graph infrastructure

Provide a general directed dependency graph:

```csharp
public interface IMarkupDependencyGraph
{
    void SetDependencies(
        MarkupDocumentId document,
        IReadOnlyCollection<MarkupDocumentId> dependencies);

    IReadOnlySet<MarkupDocumentId> GetDependencies(
        MarkupDocumentId document);

    IReadOnlySet<MarkupDocumentId> GetDependents(
        MarkupDocumentId document);
}
```

The base package provides graph behavior but does not know what creates a dependency. `ArxisStudio.Markup.Xaml` discovers XAML-specific dependencies.

### Explicit non-responsibilities

`ArxisStudio.Markup` must not:

- parse XML or XAML;
- create Avalonia objects;
- classify Avalonia properties;
- resolve CLR types;
- load resources or templates;
- inspect solutions or projects;
- invoke a compiler;
- provide UI components.

## Package 2: ArxisStudio.Markup.Xaml

`ArxisStudio.Markup.Xaml` provides a lossless XAML syntax model, semantic document services that do not require Avalonia runtime types, editing operations, and round-trip serialization.

Namespace:

```csharp
namespace ArxisStudio.Markup.Xaml;
```

### Responsibilities

#### Lossless lexer

Implement a lexer that retains tokens and trivia required to reproduce the original document.

Token categories must include at least:

- opening and closing angle brackets;
- element and attribute names;
- namespace prefixes;
- `=`;
- single and double quotes;
- attribute value text;
- whitespace;
- newlines;
- comments;
- CDATA;
- processing instructions;
- XML declaration;
- entity references;
- text content;
- malformed or skipped text.

The lexer must not silently normalize whitespace or entities.

#### Lossless parser

Build a syntax tree from lexer tokens.

Suggested public model:

```csharp
public abstract class XamlSyntaxNode;
public sealed class XamlDocument : XamlSyntaxNode;
public sealed class XamlElement : XamlSyntaxNode;
public sealed class XamlAttribute : XamlSyntaxNode;
public sealed class XamlText : XamlSyntaxNode;
public sealed class XamlComment : XamlSyntaxNode;
public sealed class XamlNamespaceDeclaration : XamlSyntaxNode;
```

Every syntax node must provide:

- its source span;
- its full span including trivia where appropriate;
- parent access;
- child enumeration;
- document identity;
- original source text;
- syntax diagnostics;
- a way to determine whether it was changed or synthesized.

Malformed documents should produce a best-effort tree and diagnostics rather than only throwing an exception.

Exceptions are appropriate for invalid API use, not ordinary user syntax errors.

#### Qualified names and namespaces

Provide explicit XAML-name types:

```csharp
public readonly record struct XamlQualifiedName(
    string? Prefix,
    string LocalName);

public sealed class XamlNamespaceContext;
```

Namespace handling must:

- resolve prefixes by scope;
- support a default namespace;
- preserve the original prefix;
- compare semantic namespaces by URI;
- handle shadowed namespace declarations;
- preserve unknown namespace URIs.

Define well-known namespace constants:

```csharp
public static class XamlNamespaces
{
    public const string Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    public const string Design =
        "http://schemas.microsoft.com/expression/blend/2008";

    public const string MarkupCompatibility =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";
}
```

Do not rely on prefixes being named `x`, `d`, or `mc`.

#### XAML directives

Recognize while preserving:

- `x:Class`;
- `x:Name`;
- `x:Key`;
- `x:DataType`;
- `x:TypeArguments`;
- `x:CompileBindings`;
- `x:Null`;
- `x:Static`;
- unknown directives.

Unknown directives must survive round-trip editing.

#### XAML values

Represent distinct value forms:

```csharp
public abstract record XamlValue;
public sealed record XamlLiteralValue(string Text) : XamlValue;
public sealed record XamlMarkupExtensionValue(...) : XamlValue;
public sealed record XamlObjectElementValue(...) : XamlValue;
public sealed record XamlUnsetValue : XamlValue;
```

The API must not collapse every attribute value into a converted CLR value.

#### Markup-extension parser

Parse markup extensions such as:

```xml
Text="{Binding Customer.Name}"
Background="{DynamicResource SurfaceBrush}"
Theme="{StaticResource PrimaryButtonTheme}"
Text="{Binding Value, Converter={StaticResource PriceConverter}}"
```

The parser must support:

- extension type names;
- positional arguments;
- named arguments;
- nested extensions;
- escaped brace sequences;
- quoted argument values;
- whitespace preservation;
- incomplete expressions with diagnostics.

Do not execute markup extensions in this package.

#### Property-element syntax

Represent property elements without requiring resolved CLR metadata:

```xml
<Button.Background>
    <SolidColorBrush Color="Red" />
</Button.Background>
```

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
</Grid.RowDefinitions>
```

The syntax package may identify the `Owner.Member` shape but must not claim that the member is a Styled, Direct, Attached, CLR, or event member. That classification belongs to Loader.

#### Element model and paths

A property element is a member of its parent rather than one of the things beside it: it produces no object, it cannot be named, and it cannot change places with a control. Every caller needs that distinction, so publish it rather than leaving each one to filter for itself:

```csharp
IEnumerable<XamlElement> content = element.ContentElements;
IEnumerable<XamlElement> members = element.MemberElements;

int position = element.IndexInContent;
string? identity = element.Identity;      // x:Name, then a literal Name
```

`Identity` is the rule the loader pairs objects by across an edit. `x:Key` is not part of it: a key is where a resource is filed, not what an element is called.

Provide a reference to an element that survives the document being edited, because a tool that remembers a selection cannot remember an element — an edit replaces every element in the document — and remembering a span is worse, since an edit above one moves it:

```csharp
public readonly record struct XamlPathStep(string? MemberName, int Index);

public sealed class XamlElementPath
{
    public static XamlElementPath Root { get; }
    public static XamlElementPath Of(XamlElement element);
    public XamlElement? Resolve(XamlDocument document);
    public XamlElementPath? Parent { get; }
    public ImmutableArray<XamlPathStep> Steps { get; }
}
```

A step is either a position among content children or a member name and a position inside it, so an element inside `<Border.Resources>` is addressable. Equality and hashing are by value, so a path can key a dictionary of expanded nodes. A property element has no path of its own.

#### Design-time namespaces

Preserve and expose:

- `d:DesignWidth`;
- `d:DesignHeight`;
- `d:DataContext`;
- `mc:Ignorable`;
- arbitrary design-time shadow attributes such as `d:Text`.

Applying design-time values to runtime objects belongs to Loader.

#### Resource and style dependency analysis

Discover XAML document dependencies created by:

```xml
<ResourceInclude Source="../Themes/Colors.axaml" />
<StyleInclude Source="avares://Controls/Themes/Generic.axaml" />
```

Requirements:

- relative URI resolution against document `BaseUri`;
- absolute URI preservation;
- `avares://` URI preservation;
- nested include discovery;
- cycle diagnostics;
- dependency graph updates after document edits;
- no requirement for a physical file.

Actual loading and application of resource objects belongs to Loader.

#### Editing API

Provide structured edits:

```csharp
document.SetAttribute(
    element,
    XamlQualifiedName.Parse("Grid.Row"),
    new XamlLiteralValue("2"));

document.SetAttribute(
    element,
    XamlQualifiedName.Parse("Text"),
    XamlValue.Parse("{Binding Customer.Name}"));

document.RemoveAttribute(element, name);
document.InsertElement(parent, index, child);
document.RemoveElement(element);
document.MoveElement(element, newParent, index);
document.ReplaceElement(element, xaml);
document.WrapElement(element, wrapperXaml);
document.UnwrapElement(element);
document.DuplicateElement(element, XamlDuplicateNames.Remove);
```

`index` counts content children only. A property element is not a position, so index 0 in a parent that declares one means before its first content child and after the member — which is what a caller asking for "first" means, and what counting `Elements` got wrong.

Duplicating takes the names out of the copy by default: a name scope refuses a second `x:Name`, so a copy that kept them would not load. `x:Key` is carried as written and collides in the same way, which is the caller's to resolve.

Every edit must:

- validate that target nodes belong to the expected document version;
- participate in a workspace transaction;
- produce minimal text changes where practical;
- update source spans after commit;
- preserve unrelated text;
- produce predictable diagnostics rather than corrupting the tree.

#### Serialization

Provide two modes:

```csharp
public enum XamlWriteMode
{
    Preserve,
    Format
}
```

`Preserve`:

- keeps unchanged source regions exactly;
- writes only changed or synthesized nodes;
- is the default mode.

`Format`:

- formats the complete document using explicit options;
- is never implicitly enabled;
- must not be required for a valid save.

Suggested formatting options:

```csharp
public sealed class XamlFormattingOptions
{
    public string Indentation { get; init; } = "    ";
    public string NewLine { get; init; } = Environment.NewLine;
    public char AttributeQuote { get; init; } = '"';
    public bool PutAttributesOnSeparateLines { get; init; }
}
```

#### Events as source members

This package preserves declarations such as:

```xml
<Button Click="SaveClicked" />
```

Without CLR metadata, it should treat `Click` as an unresolved XAML member. Loader later confirms whether it is an event.

### Explicit non-responsibilities

`ArxisStudio.Markup.Xaml` must not:

- instantiate Avalonia objects;
- execute type converters or markup extensions;
- classify Avalonia properties;
- attach event handlers;
- resolve `x:Class` to a CLR type;
- load assemblies;
- inspect projects or NuGet packages;
- run MSBuild;
- provide a visual editor.

## Package 3: ArxisStudio.Markup.Xaml.Loader

`ArxisStudio.Markup.Xaml.Loader` connects `XamlDocument` instances to live Avalonia objects.

Namespace:

```csharp
namespace ArxisStudio.Markup.Xaml.Loader;
```

### Responsibilities

#### Load environment

All external dependencies must enter through an explicit environment:

```csharp
public sealed class XamlLoadEnvironment
{
    public required IXamlSourceProvider SourceProvider { get; init; }
    public required IXamlAssemblyResolver AssemblyResolver { get; init; }
    public required IXamlTypeResolver TypeResolver { get; init; }
    public required IXamlResourceResolver ResourceResolver { get; init; }
    public IXamlRootInstanceFactory? RootInstanceFactory { get; init; }
    public IServiceProvider? Services { get; init; }
}
```

Provide usable defaults for:

- already loaded assemblies;
- explicitly supplied assemblies;
- directories explicitly supplied by the caller;
- file resources;
- in-memory XAML resources;
- Avalonia embedded resources available from loaded assemblies.

Do not inspect `.csproj`, `.sln`, `project.assets.json`, or NuGet directories.

#### Assembly resolution

```csharp
public interface IXamlAssemblyResolver
{
    ValueTask<Assembly?> ResolveAsync(
        AssemblyName assemblyName,
        CancellationToken cancellationToken);
}
```

Initial implementations:

- `ExplicitAssemblyResolver`;
- `LoadedAssemblyResolver`;
- `DirectoryAssemblyResolver`;
- `CompositeAssemblyResolver`.

The caller may explicitly provide an assembly obtained from a NuGet package. Discovering that package is outside current scope.

#### Type resolution

```csharp
public interface IXamlTypeResolver
{
    ValueTask<XamlTypeResolution> ResolveAsync(
        XamlTypeName typeName,
        XamlNamespaceContext namespaceContext,
        CancellationToken cancellationToken);
}
```

Support:

- Avalonia's default XML namespace;
- `using:Namespace`;
- `clr-namespace:Namespace`;
- optional `assembly=AssemblyName`;
- `XmlnsDefinitionAttribute`;
- standard Avalonia types;
- custom controls from explicitly available assemblies;
- generic and nested type diagnostics where applicable.

#### XAML load session

```csharp
public sealed class XamlLoadSession : IAsyncDisposable
{
    public XamlDocument Document { get; }
    public object RootObject { get; }

    public object? GetObject(XamlElement element);
    public XamlElement? GetElement(object runtimeObject);
}
```

Create sessions using a public factory:

```csharp
var session = await XamlLoadSession.CreateAsync(
    document,
    environment,
    new XamlLoadOptions
    {
        Mode = XamlLoadMode.Design
    },
    cancellationToken);
```

All Avalonia object creation and mutation must happen on the appropriate Avalonia UI thread. The API must fail clearly when called from an invalid thread or provide an injected dispatcher abstraction.

#### Object-to-node mapping

Maintain a mapping between source elements and created objects:

```csharp
object? runtimeObject = session.GetObject(element);
XamlElement? sourceElement = session.GetElement(runtimeObject);
```

Mapping must account for:

- `x:Name`;
- name scopes;
- content properties;
- collection members;
- resources that are not controls;
- objects created by styles and templates;
- template-generated visual children;
- runtime-generated objects with no source node.

Expose origin metadata:

```csharp
public enum XamlObjectOrigin
{
    Document,
    Resource,
    Style,
    Template,
    RuntimeGenerated
}
```

Do not falsely map template-generated objects to the control instance declaration.

#### Member classification

Resolve XAML members as:

```csharp
public enum XamlMemberKind
{
    StyledProperty,
    DirectProperty,
    AttachedProperty,
    ClrProperty,
    Event,
    Content,
    Collection,
    Unknown
}
```

Support:

- Avalonia `StyledProperty`;
- Avalonia `DirectProperty`;
- read-only direct properties;
- Avalonia attached properties;
- attached CLR accessor patterns;
- ordinary CLR properties;
- routed events;
- CLR events;
- collection and content members.

Every resolved member descriptor must report:

- declaring/owner type;
- target type;
- value type;
- whether it can be read;
- whether it can be written;
- whether it is attached;
- whether it is read-only;
- underlying Avalonia property, CLR property, or event metadata.

A tool offering a property list must be able to ask which members a type has rather than keeping a table of names:

```csharp
ImmutableArray<XamlMemberDescriptor> members = session.GetMembers(target);
```

Registered Avalonia properties, attached properties under their written `Owner.Member` names, and public CLR properties. Which of them are worth showing stays the tool's decision. The answer is not cached as a whole, because Avalonia registers an attached property in the static constructor of the type that declares it: `Grid.Row` becomes a member of every control only once something has caused `Grid` to be initialised.

#### Controlled property editing

Preferred edits must go through the session:

```csharp
session.SetValue(
    button,
    Layoutable.WidthProperty,
    320d);
```

Or through an XAML-aware value:

```csharp
session.SetXamlValue(
    textBox,
    TextBox.TextProperty,
    XamlValue.Parse("{Binding Customer.Name}"));
```

One operation must:

1. validate the member and target;
2. validate write access;
3. convert or parse the requested value;
4. update the runtime object;
5. update the corresponding XAML node;
6. participate in the current transaction;
7. emit diagnostics and change notifications.

If any required step fails, the operation must not leave document and object state silently inconsistent.

Text is converted the way loading converts it — the member's `TypeConverter`, or the public static `Parse` that Avalonia types such as `Thickness` and `CornerRadius` are read by instead. Text the member cannot hold is an ordinary user error: a diagnostic with the attribute's span, the objects left as they were, and nothing thrown.

#### Runtime values versus source expressions

The loader must distinguish:

- source XAML value;
- local runtime value;
- effective runtime value;
- binding expression;
- static-resource reference;
- dynamic-resource reference;
- design-time override;
- style-provided value;
- inherited value.

Example:

```xml
<TextBox Text="{Binding Customer.Name}" />
```

If the runtime text is currently `Alice`, saving must preserve:

```xml
Text="{Binding Customer.Name}"
```

It must not write:

```xml
Text="Alice"
```

unless the caller explicitly replaces the binding with a literal.

#### Detecting direct runtime changes

Provide an optional and conservative change-detection API:

```csharp
IReadOnlyList<XamlObjectChange> changes =
    session.DetectChanges();
```

It must not treat every difference in effective property value as a source edit.

Only supported local writable members may be proposed. Values produced by bindings, resources, styles, inheritance, animation, coercion, or templates must not be written automatically.

Applying detected changes must require an explicit call.

#### x:Class

Support:

```xml
<UserControl
    x:Class="MyApplication.Views.CustomerView">
```

Required behavior:

1. read the `x:Class` directive;
2. resolve the CLR type;
3. validate compatibility with the root XAML element;
4. request a root instance;
5. pass the instance to Avalonia runtime loading;
6. preserve `x:Class` during all document edits;
7. allow Avalonia to resolve declared event handlers;
8. produce clear diagnostics on failure.

Root creation must be extensible:

```csharp
public interface IXamlRootInstanceFactory
{
    ValueTask<object> CreateAsync(
        Type rootType,
        XamlRootInstanceContext context,
        CancellationToken cancellationToken);
}
```

Do not use unsafe uninitialized-object creation as a default strategy.

Document the double-initialization risk for classes whose constructors call `InitializeComponent()`. A caller-provided factory must be able to create an instance intended for runtime population.

#### Events

Event declarations must remain in the source:

```xml
<Button Click="SaveClicked" />
```

Loader should support normal Avalonia event hookup when a compatible `x:Class` root instance is provided.

The library does not suppress, remove, or intercept events. Input interception is the responsibility of a future consumer, not this repository.

#### Load modes and design-time attributes

```csharp
public enum XamlLoadMode
{
    Runtime,
    Design
}
```

In `Design` mode:

- honor `d:DesignWidth`;
- honor `d:DesignHeight`;
- apply `d:DataContext`;
- support `Design.DataContext`;
- support library-defined design-time shadow attributes such as `d:Text`;
- set Avalonia design mode as required by public APIs;
- instantiate real custom controls.

In `Runtime` mode:

- ignore design-only values according to `mc:Ignorable`;
- use ordinary XAML values and bindings.

The loader does not implement a designer UI.

#### Resources, styles, and templates

Support:

- `ResourceDictionary`;
- merged dictionaries;
- `ResourceInclude`;
- `StyleInclude`;
- `StaticResource`;
- `DynamicResource`;
- styles and selectors;
- setters;
- `ControlTheme`;
- `ControlTemplate`;
- `DataTemplate`;
- nested resource dependencies;
- relative URIs;
- `avares://` URIs when assemblies/resources are explicitly resolvable.

Resource source resolution:

```csharp
public interface IXamlResourceResolver
{
    ValueTask<XamlResource?> ResolveAsync(
        Uri resourceUri,
        Uri? baseUri,
        CancellationToken cancellationToken);
}
```

Provider priority should allow an in-memory edited document to override a file or embedded resource.

#### Resource updates

The loader must support document updates without requiring compilation when only XAML changes:

```csharp
await session.ApplyDocumentUpdateAsync(
    updatedDocument,
    cancellationToken);
```

Use the smallest safe update strategy:

| Change | Expected strategy |
| --- | --- |
| Literal writable property | Set the property directly |
| Design-time shadow value | Update the design value |
| Dynamic resource | Replace/update resource |
| Style setter | Reload affected style |
| Control theme | Reload affected theme |
| Control template | Recreate affected template content |
| Static resource | Reload the dependent object/subtree |
| Element structure | Reload affected subtree |
| Root or `x:Class` | Recreate the root session |

If an update fails:

- preserve the last successful runtime tree when possible;
- publish diagnostics;
- do not silently discard the new source document;
- allow a later corrected update.

#### Runtime diagnostics

Loader diagnostics should cover at least:

- unresolved type;
- unresolved assembly;
- unresolved member;
- incompatible member value;
- read-only property;
- missing resource;
- cyclic resource include;
- incompatible `x:Class`;
- missing event handler;
- invalid root factory result;
- type-converter failure;
- markup-extension failure;
- runtime constructor failure;
- invalid thread access.

### Explicit non-responsibilities

`ArxisStudio.Markup.Xaml.Loader` must not:

- open or parse `.sln`;
- open or evaluate `.csproj`;
- read `project.assets.json`;
- search NuGet package caches;
- install or restore packages;
- invoke MSBuild;
- compile custom C# code;
- create a visual designer;
- intercept pointer or keyboard input;
- sandbox arbitrary user code.

## Public API usage sketches

These sketched the intended shape before there was an implementation, and the implementation kept it. They are left here because they say what the boundaries are for; [`docs/api/`](docs/api/README.md) is where the API is actually documented, with examples that compile against the published surface.

### Parse and preserve a document

```csharp
var document = XamlDocument.Parse(
    sourceText,
    new XamlParseOptions
    {
        DocumentUri = new Uri(
            "file:///Views/MainView.axaml")
    });

string unchanged = document.GetText(
    XamlWriteMode.Preserve);
```

`unchanged` must equal the original source.

### Edit a syntax value

```csharp
XamlElement button = document
    .DescendantElements()
    .Single(x => x.GetDirective("Name") == "SaveButton");

document.SetAttribute(
    button,
    XamlQualifiedName.Parse("Width"),
    new XamlLiteralValue("320"));

string updated = document.GetText(
    XamlWriteMode.Preserve);
```

Only the `Width` value should change.

### Load an Avalonia object tree

```csharp
var environment = new XamlLoadEnvironment
{
    SourceProvider = sourceProvider,
    ResourceResolver = resourceResolver,
    AssemblyResolver = new CompositeAssemblyResolver(
        new LoadedAssemblyResolver(),
        new ExplicitAssemblyResolver(
            typeof(MyCustomControl).Assembly)),
    TypeResolver = typeResolver,
    RootInstanceFactory = rootFactory
};

await using var session =
    await XamlLoadSession.CreateAsync(
        document,
        environment,
        new XamlLoadOptions
        {
            Mode = XamlLoadMode.Design
        },
        cancellationToken);

var root = session.GetRoot<Control>();
```

### Edit through the runtime session

```csharp
XamlEditResult result = session.SetValue(
    selectedButton,
    Layoutable.WidthProperty,
    320d);

await File.WriteAllTextAsync(
    path,
    session.Document.GetText(),
    cancellationToken);
```

The runtime object and source document must remain synchronized. Where a document is written to is the host's business, so there is no `Save` on a document; a tool with an undo history writes through `XamlWorkspace` instead, which is what puts the edit in the history — see `docs/adr/0007-undo-belongs-to-the-workspace.md`.

### Update an in-memory resource file

```csharp
inMemorySourceProvider.Update(
    resourceUri,
    newSourceText);

await session.ApplySourceUpdateAsync(
    resourceUri,
    cancellationToken);
```

Dependent styles, themes, templates, and controls should update without compiling or launching a user application.

## Error-handling policy

Expected user errors must normally produce diagnostics rather than unstructured exceptions.

Use exceptions for:

- invalid library API usage;
- disposed sessions;
- impossible internal invariants;
- cancellation;
- unrecoverable runtime failures where no structured result can be returned.

Do not:

- swallow exceptions;
- return `null` without a reason where a result type is appropriate;
- parse exception strings to determine error categories;
- leave a partially mutated document after a failed transaction.

Prefer result models:

```csharp
public sealed class XamlLoadResult
{
    public object? RootObject { get; init; }
    public required IReadOnlyList<MarkupDiagnostic> Diagnostics { get; init; }
    public bool Success => RootObject is not null &&
        Diagnostics.All(x => x.Severity != MarkupDiagnosticSeverity.Error);
}
```

## Threading and cancellation

- Parsing and text editing should be UI-thread independent.
- Avalonia object creation and mutation must respect Avalonia thread affinity.
- Async APIs must accept `CancellationToken`.
- Do not block on async operations with `.Result` or `.Wait()`.
- Workspace snapshots must support safe concurrent reads.
- Mutations to one document/session must be serialized or guarded explicitly.

## Performance requirements

The first implementation should prioritize correctness, but the architecture must avoid obvious scaling limitations.

Target scenarios:

- documents from a few lines to several megabytes;
- resource graphs containing hundreds of XAML files;
- frequent small text updates;
- property edits that do not require full document serialization;
- resource updates that do not require full application restart.

Performance principles:

- immutable source snapshots;
- incremental text changes;
- lazy semantic analysis;
- cached namespace scopes;
- cached type/member descriptors;
- dependency-based invalidation;
- no reflection scan of every loaded assembly for every lookup;
- no full workspace reload for one changed document;
- no full formatting pass in preserve mode.

Add benchmarks after correctness milestones for:

- lexing;
- parsing;
- unchanged round-trip;
- one-attribute edit;
- markup-extension parsing;
- namespace resolution;
- type/member resolution;
- resource dependency invalidation.

## Testing strategy

### ArxisStudio.Markup.Tests

Test:

- `TextSpan`;
- line mapping;
- source snapshots;
- text changes;
- versioning;
- provider precedence;
- transactions;
- rollback;
- undo/redo;
- dependency graph behavior;
- concurrent snapshot reads.

### ArxisStudio.Markup.Xaml.Tests

Use golden/snapshot test files.

Required categories:

- unchanged byte-for-byte round-trip;
- whitespace and indentation;
- comments;
- single and double quotes;
- namespace scope and shadowing;
- unknown namespaces;
- malformed XML recovery;
- XAML directives;
- literal values;
- nested markup extensions;
- escaped braces;
- property-element syntax;
- attached-member syntax;
- resource includes;
- style includes;
- cyclic includes;
- `d:` and `mc:`;
- events as unresolved members;
- minimal one-attribute edits;
- insertion/removal/move operations;
- full formatting mode.

Add randomized/fuzz tests for lexer/parser termination. Invalid input must not cause infinite loops or unbounded recursion.

### ArxisStudio.Markup.Xaml.Loader.Tests

Run Avalonia tests in a supported headless test environment where appropriate.

Required categories:

- standard Avalonia control loading;
- custom control loading from an explicit assembly;
- StyledProperty read/write;
- DirectProperty read/write;
- read-only DirectProperty diagnostics;
- AttachedProperty read/write;
- CLR property read/write;
- routed event and CLR event resolution;
- `x:Class` resolution;
- root-instance factory;
- event-handler hookup;
- binding preservation;
- compiled-binding preservation;
- StaticResource preservation;
- DynamicResource update;
- ResourceInclude;
- StyleInclude;
- styles;
- ControlTheme;
- ControlTemplate;
- DataTemplate;
- design-time values;
- source-to-object mapping;
- template-generated object origin;
- conservative runtime change detection;
- failed update preserving last successful runtime state.

Tests must include controls from a separate test assembly to verify custom assembly resolution.

## Coding standards

- Use modern C# supported by the selected target framework.
- Enable nullable reference types.
- Treat warnings as errors.
- Add XML documentation to all public APIs.
- Prefer immutable public models.
- Keep reflection behind cached resolver services.
- Avoid global mutable state.
- Avoid service locators in new ArxisStudio APIs.
- Use dependency injection through explicit constructors/options.
- Do not expose Avalonia internal types.
- Do not expose MSBuild or NuGet concepts in current public APIs.
- Do not add speculative abstraction layers without a concrete use in the three current packages.
- Add tests with every functional change.

## Implementation milestones

Development should proceed in small, reviewable phases. Do not attempt to implement the entire system in one change.

### Milestone 0: Repository foundation

- Create solution and project structure.
- Add central package management.
- Add nullable and warnings-as-errors settings.
- Add test projects.
- Add CI for restore, build, and test.
- Add basic package metadata.
- Add API documentation generation.

Exit criteria:

- clean build;
- all test projects run;
- package dependency direction is enforced.

### Milestone 1: Markup text model

- Implement source text and spans.
- Implement line mapping.
- Implement document IDs and versions.
- Implement in-memory and file providers.
- Implement workspace document snapshots.
- Add complete unit coverage.

Exit criteria:

- efficient immutable document updates;
- stable versions;
- no XAML dependency in `ArxisStudio.Markup`.

### Milestone 2: Transactions and dependency graph

- Implement text/document changes.
- Implement transactions.
- Implement rollback.
- Implement undo/redo.
- Implement dependency graph infrastructure.

Exit criteria:

- multi-document atomic transaction tests pass;
- rollback never leaves partial changes.

### Milestone 3: Lossless XAML lexer and parser

- Implement lossless tokens and trivia.
- Implement syntax nodes.
- Implement namespace declarations.
- Implement diagnostics and malformed-input recovery.
- Implement unchanged preserve-mode writer.

Exit criteria:

- unchanged golden files round-trip exactly;
- parser terminates on fuzzed invalid input.

### Milestone 4: XAML values and editing

- Implement directive recognition.
- Implement markup-extension parser.
- Implement property-element representation.
- Implement structured attribute and element edits.
- Implement minimal preserve-mode text changes.
- Implement explicit full formatting mode.

Exit criteria:

- one-property edits preserve all unrelated source text;
- nested markup-extension tests pass.

### Milestone 5: XAML resource graph

- Implement `ResourceInclude` discovery.
- Implement `StyleInclude` discovery.
- Resolve relative URIs.
- Detect cycles.
- Update dependencies incrementally.

Exit criteria:

- nested and cyclic resource graph tests pass;
- no Avalonia runtime dependency is introduced.

### Milestone 6: Avalonia loader foundation

- Add public Avalonia runtime-loader dependency.
- Implement load environment.
- Implement source, assembly, type, and resource resolvers.
- Implement standard and custom control loading.
- Enforce Avalonia thread affinity.

Exit criteria:

- a document with standard controls loads;
- a custom control from an explicitly supplied assembly loads;
- no project-system discovery exists.

### Milestone 7: Runtime mapping and properties

- Implement node/object mapping.
- Implement member descriptors.
- Support Styled, Direct, Attached, CLR, event, content, and collection members.
- Implement controlled `SetValue`.
- Synchronize source and runtime changes transactionally.

Exit criteria:

- property matrix tests pass;
- read-only properties produce diagnostics;
- bindings are not overwritten by effective values.

### Milestone 8: x:Class and events

- Implement CLR root-type resolution.
- Implement root-instance factory.
- Load into an existing root instance.
- Support event-handler hookup.
- Document constructor/`InitializeComponent` behavior.

Exit criteria:

- compatible `x:Class` loads;
- incompatible roots produce diagnostics;
- event declarations survive round trip.

### Milestone 9: Resources, styles, and templates

- Load resource dictionaries.
- Support resource/style includes.
- Support static and dynamic resources.
- Support styles, themes, and templates.
- Track resource object origins.

Exit criteria:

- external resource files affect loaded controls;
- custom control templates load correctly;
- nested dependencies work from in-memory providers.

### Milestone 10: Design mode and updates

- Apply standard design-time values.
- Apply design-time shadow values.
- Implement document/resource update strategies.
- Preserve last successful runtime state after failed updates.
- Implement conservative runtime change detection.

Exit criteria:

- XAML-only changes update without compilation;
- resource and template update tests pass;
- no designer UI or input-interception code is added.

### Milestone 11: Stabilization

- Add benchmarks.
- Add public API compatibility checks.
- Improve diagnostics.
- Add stress and leak tests.
- Document known limitations.
- Prepare preview NuGet packages.

### Milestone 12: Identity, includes at the root, and a fixed API

The first three items are limitations recorded in `docs/limitations.md` during Milestone 11. Each
is a case where the update path is conservative because it had no way to be sure, rather than
because being conservative was right.

- Give an element a declared identity — `x:Name`, or `Name` where it means the same — and pair
  elements across an update by it before falling back to position.
- Move the objects that already exist when siblings are reordered, instead of rebuilding them.
- Update a resource that is included straight into the root element, instead of asking for a new
  session.
- Carry a rebuilt resource dictionary's merged dictionaries across, not only its entries.
- Declare the public API of all three packages shipped, so that from then on a breaking change is
  visible in a reviewable diff.

Exit criteria:

- reordering named siblings preserves object identity and runtime state;
- a document whose root merges an include updates in place;
- unnamed or ambiguous siblings still fall back to the conservative behaviour;
- `PublicAPI.Shipped.txt` is populated for all three packages and the unshipped files are empty.

### Milestone 13: the seam a designer is built on

An audit against the question "are these three packages a foundation for a visual designer of
Avalonia forms and controls" found the primitives almost all present and the seam between them
missing: `MarkupWorkspace` holds the undo history and knows nothing about XAML, `XamlDocumentEditor`
expresses structured edits and knows nothing about history, and nothing in the repository put the
two together. A tool taking the packages as they were would have written an undo stack of its own
and left this one unused.

- Add the one editing operation the set was missing: replacing an element in place, as one change
  over its own span rather than a removal and an insertion.
- Add wrapping and unwrapping, which are the same family and share its indentation reasoning.
- Add `XamlWorkspace`: structured edits applied through the workspace, so one edit is one undo
  entry under a name a user would recognise, and edits to several documents are one action.
- Record which of the two write directions a tool should use, and why undo belongs to the
  workspace rather than to the tool.
- Prove it in the showcase: delete, duplicate, wrap and undo, built on the published API alone.

Exit criteria:

- an element can be replaced, wrapped and unwrapped without disturbing anything around it;
- wrapping then unwrapping returns the document character for character;
- a structured edit is one undoable action, and undoing it restores the document exactly;
- an edit spanning two documents is undone by one command;
- an editor opened on a version the workspace has moved past is refused rather than approximated;
- the showcase performs structural edits and undoes them without a line added to `src/`.

### Milestone 14: an element model for tools

Milestone 13 answered whether a designer can be built on these packages. Building one found that
the editing primitives are complete and the *model* is not: a host re-derives the same handful of
rules every time — which children produce objects, what an element is called, how to keep a
reference to one across an edit, which members a type has. The same rule appears five times inside
this repository, which is what an absent API member looks like.

- Publish the distinction between content and member elements, which every caller needs and every
  caller currently writes by hand.
- Publish the identity rule — `x:Name`, then a literal `Name` — instead of leaving it internal.
- Add a stable reference to an element that survives an edit, an undo and a redo, so a tool's
  selection and expansion state do not depend on spans or on live objects.
- Add duplication, which is otherwise three unobvious steps.
- Make an insertion index count the children a caller means, and record why that is a fix rather
  than a second method.
- Let a tool enumerate a type's members rather than inventing the list.
- Fix what enumerating found: a value written as text for a type with no `TypeConverter` — a
  `Thickness`, a `CornerRadius` — reached the setter as a string, and the exception it raised came
  out of an update instead of being reported as a diagnostic.

Exit criteria:

- inserting at index 0 into a parent that declares a property element lands before the first
  content child and after the property element;
- a path resolves to the same element after an unrelated edit, and equality is by value;
- duplicating produces a copy that loads;
- text a member cannot hold is refused with a diagnostic and a span, the objects are left as they
  were, and nothing is thrown;
- the showcase's tree, selection and property list are expressed in the published model, and the
  code it needed for them is gone.

## Definition of done for the first preview release

The first preview release is ready when all of the following are true:

- existing Avalonia XAML documents can be parsed without losing unknown content;
- unchanged documents round-trip exactly;
- individual syntax edits preserve unrelated formatting;
- standard and explicitly supplied custom Avalonia controls can be loaded;
- Styled, Direct, Attached, and CLR properties are supported;
- `x:Class` can be resolved through an explicit environment;
- event declarations are preserved;
- bindings and resource expressions are not replaced by effective runtime values;
- resource dictionaries, styles, control themes, and templates load;
- `ResourceInclude` and `StyleInclude` work through supplied source/resource providers;
- `d:` attributes are preserved and can be applied in design load mode;
- runtime and document updates are synchronized through explicit session operations;
- diagnostics have stable codes and source locations;
- no project-system, MSBuild, NuGet-management, or visual-designer functionality has entered the packages;
- automated tests cover the critical round-trip and runtime scenarios.

## Out of scope

The following are explicitly outside the scope of the three packages. A sample may demonstrate what a host builds on top of them, provided it is built on the published API and adds nothing to `src/` — see `docs/adr/0006-inspector-in-the-sample.md`.

- visual designer or form designer;
- selection adorners;
- property-inspector UI;
- drag and drop;
- pointer or keyboard interception;
- Play/Stop UI;
- `.sln`, `.slnx`, or `.csproj` discovery;
- MSBuild project evaluation;
- project creation or modification;
- NuGet search, install, update, remove, or restore;
- C# compilation;
- Roslyn source analysis;
- IDE integration;
- application packaging or deployment;
- sandboxing untrusted XAML or assemblies.

Do not create placeholder implementations for these features inside the current packages.

## Future integration boundary

Future ArxisStudio project-system packages may provide implementations of the current resolver/provider interfaces.

Conceptual future composition:

```csharp
// Future package, not part of current development.
var projectEnvironment =
    await projectSystem.CreateXamlEnvironmentAsync(project);

var session = await XamlLoadSession.CreateAsync(
    document,
    projectEnvironment,
    options,
    cancellationToken);
```

The markup libraries must not depend on future project-system package names or types.

## Instructions for the implementation agent

When starting development from this document:

1. Treat this README as the architectural contract.
2. Work only on the three current packages.
3. Begin with Milestone 0 and proceed sequentially.
4. Do not introduce MSBuild, NuGet, Roslyn, or designer functionality.
5. Keep every milestone buildable and tested.
6. Prefer small commits with one architectural purpose.
7. Do not use `XDocument` as the sole round-trip representation.
8. Do not regenerate complete XAML from runtime objects.
9. Do not replace bindings or resources with their effective values.
10. Do not use Avalonia/XamlX internal APIs without explicit approval.
11. Report any required architectural deviation before implementing it.
12. Add a test that fails before fixing every discovered bug.

The first development task is **Milestone 0: Repository foundation**. Do not start runtime loading until the text model and lossless XAML document model have passing tests.

