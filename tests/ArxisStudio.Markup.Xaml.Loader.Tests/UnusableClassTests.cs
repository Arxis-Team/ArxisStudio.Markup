using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A document whose <c>x:Class</c> the environment cannot give it — a form nobody has built yet,
/// or a class that is not what the root says it is — is still shown, as the element its root is
/// written as, and goes on following its document.
/// </summary>
/// <remarks>
/// The class has always been reported and then carried on without, in intent: what was not
/// carried on without was the directive itself, which went to Avalonia in the projected text, and
/// Avalonia's loader resolves it on its own and fails the whole document. A form the project has
/// not built yet is the first thing a designer opens.
/// </remarks>
public sealed class UnusableClassTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;
    private const string NotBuilt = "Contoso.Views.NotBuiltYet";

    private const string Form =
        "  <StackPanel>\n" +
        "    <TextBlock x:Name=\"Title\" Text=\"hello\" />\n" +
        "    <Button Content=\"Save\" Click=\"SaveClicked\" />\n" +
        "  </StackPanel>\n";

    private static readonly Uri ViewUri = new("file:///Views/NotBuiltYet.axaml");
    private static readonly Uri ColorsUri = new("file:///Themes/Colors.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(CustomerView).Assembly], new InMemoryMarkupSourceProvider());

    private static string View(string content, string className = NotBuilt, string root = "UserControl") =>
        $"<{root} xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"        x:Class=\"{className}\">\n" +
        content +
        $"</{root}>";

    private static async Task<XamlLoadSession> LoadAsync(
        string xaml,
        XamlLoadMode mode = XamlLoadMode.Design,
        XamlLoadEnvironment? environment = null)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            environment ?? Environment(),
            new XamlLoadOptions { Mode = mode },
            TestContext.Current.CancellationToken);

        Assert.True(
            session is not null,
            "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    [AvaloniaTheory]
    [InlineData(XamlLoadMode.Design)]
    [InlineData(XamlLoadMode.Runtime)]
    public async Task AClassNotBuiltYetStillShowsItsMarkup(XamlLoadMode mode)
    {
        await using XamlLoadSession session = await LoadAsync(View(Form), mode);

        // Built as what the root is written as, because there is no class for it to be.
        Assert.Equal(typeof(UserControl), session.RootObject.GetType());

        var panel = Assert.IsType<StackPanel>(session.GetRoot<UserControl>().Content);

        Assert.Equal(2, panel.Children.Count);

        // Said rather than hidden — and as a warning, because the load went on without it.
        MarkupDiagnostic unresolved = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.UnresolvedRootType);

        Assert.Equal(MarkupDiagnosticSeverity.Warning, unresolved.Severity);
        Assert.Contains(NotBuilt, unresolved.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);

        // The document still says what its author wrote; only the text Avalonia was given did not.
        Assert.Contains($"x:Class=\"{NotBuilt}\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task WithoutTheClassNamesStillResolveAndHandlersAreReportedNotHookedUp()
    {
        await using XamlLoadSession session = await LoadAsync(View(Form));

        var root = session.GetRoot<UserControl>();

        // A name scope needs no class: x:Name registers with the root whatever the root is.
        Assert.IsType<TextBlock>(root.FindControl<TextBlock>("Title"));

        // A handler names a method of the class, and with no class there is nothing it names.
        MarkupDiagnostic handler = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);

        Assert.Contains("SaveClicked", handler.Message, StringComparison.Ordinal);
        Assert.Contains("Click=\"SaveClicked\"", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheRootAndWhatItHoldsAreMappedToTheirElements()
    {
        await using XamlLoadSession session = await LoadAsync(View(Form));

        XamlElement root = Assert.IsType<XamlElement>(session.Document.Root);
        XamlElement button = root.DescendantElements().First(static e => e.Name.LocalName == "Button");

        Assert.Same(session.RootObject, session.Objects.GetObject(root));
        Assert.IsType<Button>(session.Objects.GetObject(button));
    }

    [AvaloniaFact]
    public async Task AClassThatIsNotWhatTheRootSaysIsReportedAndTheRootIsBuiltAsWritten()
    {
        // CustomerView is a UserControl, so a document rooted at Button cannot populate it.
        await using XamlLoadSession session = await LoadAsync(
            View(string.Empty, typeof(CustomerView).FullName!, "Button"));

        Assert.Equal(typeof(Button), session.RootObject.GetType());

        // This one is the document contradicting itself rather than the project lagging behind
        // it, and it stays an error.
        Assert.Contains(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.IncompatibleRootType && d.IsError);
    }

    [AvaloniaFact]
    public async Task AChildAddedToTheRootRebuildsItsContentInPlace()
    {
        // The root's content is rebuilt from a projection of the whole document, x:Class and all
        // — which is the projection the load had to be told to leave the class out of.
        string Panel(string children) => View(children, root: "StackPanel");

        await using XamlLoadSession session = await LoadAsync(Panel("  <TextBlock Text=\"one\" />\n"));

        var root = session.GetRoot<StackPanel>();

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(Panel("  <TextBlock Text=\"one\" />\n  <TextBlock Text=\"two\" />\n")),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(root, session.RootObject);
        Assert.Equal(2, root.Children.Count);
    }

    [AvaloniaFact]
    public async Task APartOfTheFormHoldingAHandlerIsRebuiltWithoutIt()
    {
        await using XamlLoadSession session = await LoadAsync(View(Form));

        var root = session.GetRoot<UserControl>();

        // StackPanel to WrapPanel is a different element, so the panel is rebuilt on its own —
        // and the button inside still names a handler that nothing could hook up.
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(View(Form.Replace("StackPanel", "WrapPanel", StringComparison.Ordinal))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(root, session.RootObject);

        var panel = Assert.IsType<WrapPanel>(root.Content);

        Assert.Equal(2, panel.Children.Count);
    }

    [AvaloniaTheory]
    [InlineData(NotBuilt)]
    [InlineData(null)]
    public async Task ASourceUpdateReadsTheDocumentTheWayItsLoadDid(string? className)
    {
        // Whatever the load withheld from Avalonia — a class it could not use, a handler with
        // nothing to hook up to — a projection made later withholds too. Otherwise the two texts
        // differ when nothing has, and an include that did not change rebuilds what it reaches.
        var resources = new InMemoryResourceResolver();
        XamlLoadEnvironment defaults = Environment();

        resources.Update(
            ColorsUri,
            $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\"\n" +
            $"                    xmlns:x=\"{XamlNamespace}\">\n" +
            "  <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "</ResourceDictionary>");

        var environment = new XamlLoadEnvironment
        {
            SourceProvider = defaults.SourceProvider,
            AssemblyResolver = defaults.AssemblyResolver,
            TypeResolver = defaults.TypeResolver,
            ResourceResolver = new CompositeResourceResolver(resources, defaults.ResourceResolver),
        };

        string classAttribute = className is null ? string.Empty : $" x:Class=\"{className}\"";

        string xaml =
            $"<Border xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"{classAttribute}>\n" +
            "  <Border.Resources>\n" +
            "    <ResourceDictionary>\n" +
            "      <ResourceDictionary.MergedDictionaries>\n" +
            "        <ResourceInclude Source=\"/Themes/Colors.axaml\" />\n" +
            "      </ResourceDictionary.MergedDictionaries>\n" +
            "    </ResourceDictionary>\n" +
            "  </Border.Resources>\n" +
            "  <Button Content=\"Save\" Click=\"NotWrittenYet\" />\n" +
            "</Border>";

        await using XamlLoadSession session = await LoadAsync(xaml, environment: environment);

        XamlUpdateResult unchanged = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        Assert.True(unchanged.Applied, string.Join(" | ", unchanged.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.None, unchanged.Strategy);

        // A document update leaves its own projection behind, and that is what the next source
        // update compares against — so it has to have been made the same way too.
        XamlUpdateResult edited = await session.ApplyDocumentUpdateAsync(
            Parse(xaml.Replace("Content=\"Save\"", "Content=\"Store\"", StringComparison.Ordinal)),
            TestContext.Current.CancellationToken);

        Assert.True(edited.Applied, string.Join(" | ", edited.Diagnostics));

        XamlUpdateResult afterEdit = await session.ApplySourceUpdateAsync(
            ColorsUri, TestContext.Current.CancellationToken);

        Assert.Equal(XamlUpdateStrategy.None, afterEdit.Strategy);
    }
}
