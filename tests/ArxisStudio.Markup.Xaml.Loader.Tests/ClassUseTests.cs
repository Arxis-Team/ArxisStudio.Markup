using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A host says whether the class a document's <c>x:Class</c> names is constructed. An application's
/// document is wanted for what it declares — its styles and resources — and its class, constructed,
/// would be the user's startup code running inside the host.
/// </summary>
public sealed class ClassUseTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;

    private static readonly Uri AppUri = new("file:///App.axaml");
    private static readonly Uri ViewUri = new("file:///Views/CustomerView.axaml");

    private static string App(string greeting = "Hello", string accent = "Red") =>
        $"<Application xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"             x:Class=\"{typeof(CountingApplication).FullName}\">\n" +
        "  <Application.Styles>\n" +
        "    <Style Selector=\"Button\">\n" +
        "      <Setter Property=\"Margin\" Value=\"4\" />\n" +
        "    </Style>\n" +
        "  </Application.Styles>\n" +
        "  <Application.Resources>\n" +
        $"    <x:String x:Key=\"Greeting\">{greeting}</x:String>\n" +
        $"    <SolidColorBrush x:Key=\"Accent\" Color=\"{accent}\" />\n" +
        "  </Application.Resources>\n" +
        "</Application>";

    private static string View() =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"             x:Class=\"{typeof(CustomerView).FullName}\">\n" +
        "  <Button Content=\"Save\" Click=\"SaveClicked\" />\n" +
        "</UserControl>";

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(CustomerView).Assembly], new InMemoryMarkupSourceProvider());

    private static XamlDocument Parse(string xaml, Uri uri) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = uri });

    private static async Task<XamlLoadSession> LoadAsync(string xaml, Uri uri, XamlClassUse classUse)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml, uri),
            Environment(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design, ClassUse = classUse },
            TestContext.Current.CancellationToken);

        Assert.True(
            session is not null,
            "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    [AvaloniaFact]
    public async Task AnApplicationLoadedAsWrittenIsAnApplicationWhoseClassIsNeverConstructed()
    {
        int before = CountingApplication.Constructions;

        await using XamlLoadSession session = await LoadAsync(App(), AppUri, XamlClassUse.AsWritten);

        Assert.Equal(typeof(Application), session.RootObject.GetType());
        Assert.Equal(before, CountingApplication.Constructions);

        var application = session.GetRoot<Application>();

        Assert.Single(application.Styles);
        Assert.Equal("Hello", application.Resources["Greeting"]);

        // Nothing is wrong with the class: the host asked for it to be left out, and nothing is said.
        Assert.DoesNotContain(
            session.Diagnostics,
            static d => d.Code is XamlLoaderDiagnosticCodes.UnresolvedRootType
                or XamlLoaderDiagnosticCodes.IncompatibleRootType);
        Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);

        // The document still says what its author wrote; only the text Avalonia was given did not.
        Assert.Contains("x:Class=", session.Document.GetText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ByDefaultTheApplicationsClassIsConstructed()
    {
        int before = CountingApplication.Constructions;

        await using XamlLoadSession session = await LoadAsync(App(), AppUri, XamlClassUse.Construct);

        Assert.IsType<CountingApplication>(session.RootObject);
        Assert.Equal(before + 1, CountingApplication.Constructions);
    }

    [AvaloniaFact]
    public async Task AHandlerOfAClassLeftOutIsReportedForWhatItIsAndLeftOut()
    {
        await using XamlLoadSession session = await LoadAsync(View(), ViewUri, XamlClassUse.AsWritten);

        Assert.Equal(typeof(UserControl), session.RootObject.GetType());
        Assert.IsType<Button>(session.GetRoot<UserControl>().Content);

        // CustomerView declares SaveClicked; it is the host that left the class out, and the
        // diagnostic says so rather than claiming the document names no class.
        MarkupDiagnostic handler = Assert.Single(
            session.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.MissingEventHandler);

        Assert.Contains("SaveClicked", handler.Message, StringComparison.Ordinal);
        Assert.Contains("as written", handler.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnUpdateOfADocumentLoadedAsWrittenStillLeavesTheClassOut()
    {
        await using XamlLoadSession session = await LoadAsync(View(), ViewUri, XamlClassUse.AsWritten);

        var root = session.GetRoot<UserControl>();

        // The root's content is rebuilt from a projection of the whole document — and that projection
        // leaves the class out as the load's did: put back, it would build a CustomerView inside the
        // UserControl, or fail on a handler with nothing to be hooked up to.
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(
                View().Replace(
                    "<Button Content=\"Save\" Click=\"SaveClicked\" />",
                    "<StackPanel><Button Content=\"Save\" Click=\"SaveClicked\" /><TextBlock Text=\"two\" /></StackPanel>",
                    StringComparison.Ordinal),
                ViewUri),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(root, session.RootObject);
        Assert.Equal(typeof(UserControl), session.RootObject.GetType());
        Assert.Equal(2, Assert.IsType<StackPanel>(root.Content).Children.Count);
        Assert.DoesNotContain(
            result.Diagnostics,
            static d => d.Code is XamlLoaderDiagnosticCodes.UnresolvedRootType
                or XamlLoaderDiagnosticCodes.IncompatibleRootType);
    }

    [AvaloniaFact]
    public async Task AnApplicationsStylesAndResourcesLoadedAsWrittenServeAControl()
    {
        // What a designer does with the result: the styles and the resources it declares are taken
        // over by a form, and a control in the form takes them. Taken over and not shared — a style
        // and a dictionary have one owner, and adding the application's own to a second throws "The
        // Style already has a parent" — so a form wanting them has a session of its own.
        await using XamlLoadSession session = await LoadAsync(App(), AppUri, XamlClassUse.AsWritten);

        var application = session.GetRoot<Application>();
        var button = new Button();
        var host = new Border { Child = button };

        Assert.Throws<InvalidOperationException>(() => host.Styles.Add(application.Styles[0]));

        var styles = application.Styles.ToArray();
        IResourceDictionary resources = application.Resources;

        application.Styles.Clear();
        application.Resources = new ResourceDictionary();

        foreach (var style in styles)
        {
            host.Styles.Add(style);
        }

        host.Resources.MergedDictionaries.Add(resources);

        var window = new Window { Content = host };

        window.Show();

        try
        {
            Assert.Equal(new Thickness(4), button.Margin);
            Assert.True(host.TryFindResource("Accent", out object? accent));
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(accent).Color);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ByDefaultAClassIsConstructed() =>
        Assert.Equal(XamlClassUse.Construct, XamlLoadOptions.Default.ClassUse);
}
