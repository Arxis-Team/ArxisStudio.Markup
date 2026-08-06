using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The exit criteria of this milestone: a compatible <c>x:Class</c> loads, an incompatible root
/// is reported, and event declarations survive a round trip.
/// </summary>
public sealed class RootClassTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;
    private const string ViewClass = "ArxisStudio.Markup.Xaml.Loader.TestControls.CustomerView";

    private static XamlLoadEnvironment Environment(IXamlRootInstanceFactory? factory = null)
    {
        XamlLoadEnvironment defaults = XamlLoadEnvironment.CreateDefault(
            [typeof(CustomerView).Assembly], new InMemoryMarkupSourceProvider());

        return factory is null
            ? defaults
            : new XamlLoadEnvironment
            {
                SourceProvider = defaults.SourceProvider,
                AssemblyResolver = defaults.AssemblyResolver,
                TypeResolver = defaults.TypeResolver,
                ResourceResolver = defaults.ResourceResolver,
                RootInstanceFactory = factory,
            };
    }

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = new Uri("file:///Views/CustomerView.axaml") });

    private static string ViewXaml(string rootElement = "UserControl", string content = "") =>
        $"<{rootElement} xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"        x:Class=\"{ViewClass}\">\n" +
        $"{content}" +
        $"</{rootElement}>";

    [AvaloniaFact]
    public async Task AHandlerNobodyHasWrittenIsReportedAndTheRestStillLoads()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Content=\"Save\" Click=\"NotWrittenYet\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Avalonia fails the whole document over this, and a handler nobody has written yet is
        // the ordinary state of a file being worked on.
        MarkupDiagnostic reported = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);

        Assert.Equal(MarkupDiagnosticSeverity.Warning, reported.Severity);
        Assert.NotNull(reported.Span);

        var view = session.GetRoot<CustomerView>();

        Assert.IsType<Button>(view.Content);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);

        // The document itself still says what its author wrote.
        Assert.Contains("Click=\"NotWrittenYet\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The root of an <c>x:Class</c> document is in the object map like everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was not, and the reason is the whole point of <c>x:Class</c>: the instance is created
    /// first and handed to Avalonia already made, so Avalonia never records where it built it — and
    /// the map pairs an object to an element by exactly that recorded position. Every child paired
    /// and the root did not, which measures as a document of five elements mapping four.
    /// </para>
    /// <para>
    /// A designer feels this immediately. Clicking a form's background selects nothing, and the
    /// properties of the form itself — a window's Title, a control's size — are the ones nothing can
    /// reach.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task TheRootOfAnXClassDocumentIsMappedToItsElement()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Content=\"Save\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        XamlElement root = Assert.IsType<XamlElement>(session.Document.Root);

        Assert.Same(session.RootObject, session.Objects.GetObject(root));
        Assert.Same(root, session.Objects.GetElement(session.RootObject));

        // The child was never the problem, and saying so keeps this test honest about which half
        // of the pairing it is defending.
        XamlElement button = Assert.Single(root.ContentElements);

        Assert.NotNull(session.Objects.GetObject(button));
    }

    [AvaloniaFact]
    public async Task AHandlerThatExistsIsNotReported()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Content=\"Save\" Click=\"SaveClicked\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);
    }

    [AvaloniaFact]
    public async Task AMistypedExtensionFromTheDocumentsOwnNamespaceIsReported()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(
                $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
                $"             xmlns:local=\"using:{typeof(CustomerView).Namespace}\"\n" +
                $"             x:Class=\"{ViewClass}\">\n" +
                "  <Button Content=\"{local:NoSuchExtension}\" />\n" +
                "</UserControl>"),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        await using (session)
        {
            // The environment's resolver is the authority for a namespace the document brought
            // in itself, so a name it cannot find is a name that will not resolve at all.
            MarkupDiagnostic reported = Assert.Single(
                result.Diagnostics,
                static d => d.Code == XamlLoaderDiagnosticCodes.MarkupExtensionFailure);

            Assert.Contains("NoSuchExtension", reported.Message, StringComparison.Ordinal);
            Assert.NotNull(reported.Span);
        }
    }

    [AvaloniaFact]
    public async Task TheExtensionsAvaloniaAndXamlDefineAreNotReported()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Content=\"{Binding Name}\" Tag=\"{x:Null}\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Avalonia's compiler finds its own extensions through machinery this library does not
        // model — IXamlTypeResolver resolves UserControl from that namespace and not Binding —
        // so a check based on it would report the commonest construct in Avalonia XAML.
        Assert.DoesNotContain(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MarkupExtensionFailure);
    }

    [AvaloniaFact]
    public async Task ACompatibleRootClassIsInstantiatedAndPopulated()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Content=\"Save\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        var view = session.GetRoot<CustomerView>();

        Assert.IsType<CustomerView>(session.RootObject);
        Assert.IsType<Button>(view.Content);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);
    }

    [AvaloniaFact]
    public async Task EventHandlersOnTheRootClassAreAttached()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml(content: "  <Button Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />\n")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        var view = session.GetRoot<CustomerView>();
        var button = (Button)view.Content!;

        Assert.Equal(0, view.SaveClickCount);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, view.SaveClickCount);
    }

    [AvaloniaFact]
    public async Task AnIncompatibleRootTypeIsReportedRatherThanLoaded()
    {
        // CustomerView is a UserControl, so a document rooted at Button cannot populate it.
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(ViewXaml("Button")),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.IncompatibleRootType && d.IsError);

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task AnUnresolvableRootClassIsReportedWithItsName()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse($"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
                  "             x:Class=\"Nowhere.At.All.MissingView\" />"),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        MarkupDiagnostic diagnostic = Assert.Single(
            result.Diagnostics, static d => d.Code == XamlLoaderDiagnosticCodes.UnresolvedRootType);

        Assert.Contains("Nowhere.At.All.MissingView", diagnostic.Message, StringComparison.Ordinal);
        Assert.NotNull(diagnostic.Span);

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task AConstructorThatThrowsIsReportedRatherThanPropagated()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse($"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
                  "             x:Class=\"ArxisStudio.Markup.Xaml.Loader.TestControls.UnconstructableView\" />"),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.RootInstanceCreationFailure);

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ACallerSuppliedFactoryIsUsedInsteadOfTheDefault()
    {
        // This is the escape hatch for types whose constructors call InitializeComponent: the
        // caller decides how the instance comes into being.
        var factory = new RecordingFactory();

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml()),
            Environment(factory),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(factory.Created, session.RootObject);
        Assert.Equal(typeof(CustomerView), factory.RequestedType);
    }

    [AvaloniaFact]
    public async Task AFactoryReturningTheWrongTypeIsReported()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(ViewXaml()),
            Environment(new WrongTypeFactory()),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.InvalidRootFactoryResult);

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task TheFactoryIsToldWhetherTheLoadIsForDesign()
    {
        var factory = new RecordingFactory();

        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse(ViewXaml()),
            Environment(factory),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.Equal(XamlLoadMode.Design, factory.RequestedMode);
    }

    [Fact]
    public void EventDeclarationsAndTheClassDirectiveSurviveARoundTrip()
    {
        // Nothing in the syntax layer resolves either, and nothing may rewrite them.
        string xaml = ViewXaml(content: "  <Button Click=\"SaveClicked\" Content=\"Save\" />\n");

        Assert.Equal(xaml, Parse(xaml).GetText());
    }

    [Fact]
    public void EditingAnUnrelatedAttributeLeavesTheClassAndHandlersAlone()
    {
        XamlDocument document = Parse(
            ViewXaml(content: "  <Button Click=\"SaveClicked\" Content=\"Save\" Width=\"100\" />\n"));

        XamlElement button = document.DescendantElements().First(static e => e.Name.LocalName == "Button");
        string edited = document.SetAttribute(button, XamlQualifiedName.Parse("Width"), "320").GetText();

        Assert.Contains($"x:Class=\"{ViewClass}\"", edited, StringComparison.Ordinal);
        Assert.Contains("Click=\"SaveClicked\"", edited, StringComparison.Ordinal);
        Assert.Contains("Width=\"320\"", edited, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEventDeclarationIsAnUnresolvedMemberToTheSyntaxLayer()
    {
        // The syntax package cannot know Click is an event, and says so by not claiming to.
        XamlDocument document = Parse(ViewXaml(content: "  <Button Click=\"SaveClicked\" />\n"));
        XamlElement button = document.DescendantElements().First(static e => e.Name.LocalName == "Button");

        XamlAttribute click = button.GetAttribute("Click")!;

        Assert.False(click.IsDirective);
        Assert.Equal("SaveClicked", click.GetValueText());
        Assert.IsType<XamlLiteralValue>(click.GetValue());
    }

    [AvaloniaFact]
    public async Task ADocumentWithoutAClassStillLoads()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            Parse($"<Button xmlns=\"{AvaloniaNamespace}\" Content=\"plain\" />"),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<Button>(session.RootObject);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);
    }

    private sealed class RecordingFactory : IXamlRootInstanceFactory
    {
        public Type? RequestedType { get; private set; }

        public XamlLoadMode RequestedMode { get; private set; }

        public object? Created { get; private set; }

        public ValueTask<object> CreateAsync(
            Type rootType, XamlRootInstanceContext context, CancellationToken cancellationToken)
        {
            RequestedType = rootType;
            RequestedMode = context.Mode;
            Created = Activator.CreateInstance(rootType)!;

            return new ValueTask<object>(Created);
        }
    }

    private sealed class WrongTypeFactory : IXamlRootInstanceFactory
    {
        public ValueTask<object> CreateAsync(
            Type rootType, XamlRootInstanceContext context, CancellationToken cancellationToken) =>
            new(new Button());
    }
}
