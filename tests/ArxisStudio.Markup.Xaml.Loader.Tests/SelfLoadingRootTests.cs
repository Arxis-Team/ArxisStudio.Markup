using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A root whose constructor loads its own markup is populated once, from the document.
/// </summary>
/// <remarks>
/// <para>
/// Every form a project writes has this shape: an <c>x:Class</c> partial whose constructor calls
/// <c>InitializeComponent()</c>. Constructing one and then populating it used to load markup
/// twice — the compiled markup inside the constructor, the document after it — and what the root
/// <em>accumulates</em> rather than assigns came out doubled. A keyed resource on the root was the
/// loud case: the second <c>Add</c> under the same key threw, and the form did not load at all.
/// </para>
/// <para>
/// The session now hands the document to the constructor's own load, through the hook Avalonia's
/// XAML compiler emits for exactly this, so there is one population and it is the document's.
/// </para>
/// </remarks>
public sealed class SelfLoadingRootTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault(
            [typeof(SelfLoadingView).Assembly], new InMemoryMarkupSourceProvider());

    /// <summary>The view's document, as somebody editing it would have it: not what was compiled.</summary>
    private static XamlDocument View(string title = "Live", string content = "", string rootAttributes = "") =>
        XamlDocument.Parse(
            $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
            $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
            $"             xmlns:d=\"{DesignNamespace}\"\n" +
            $"             x:Class=\"{typeof(SelfLoadingView).FullName}\"{rootAttributes}>\n" +
            "    <UserControl.Resources>\n" +
            "        <SolidColorBrush x:Key=\"Accent\" Color=\"Blue\" />\n" +
            "    </UserControl.Resources>\n" +
            "    <UserControl.Styles>\n" +
            "        <Style Selector=\"TextBlock\">\n" +
            "            <Setter Property=\"FontSize\" Value=\"30\" />\n" +
            "        </Style>\n" +
            "    </UserControl.Styles>\n" +
            "    <StackPanel>\n" +
            $"        <TextBlock x:Name=\"Title\" Text=\"{title}\" />\n" +
            content +
            "    </StackPanel>\n" +
            "</UserControl>",
            new XamlParseOptions { DocumentUri = new Uri("file:///Views/SelfLoadingView.axaml") });

    private static string Shown(SelfLoadingView view) =>
        ((TextBlock)((StackPanel)view.Content!).Children[0]).Text ?? string.Empty;

    [AvaloniaFact]
    public async Task ARootThatLoadsItselfIsPopulatedOnce()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            View(), Environment(), cancellationToken: TestContext.Current.CancellationToken);

        // The compiled markup declares the same key, as the document of any built project does.
        // Populating twice adds it twice, and a dictionary refuses the second.
        Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            var view = session.GetRoot<SelfLoadingView>();

            Assert.Equal(Colors.Blue, ((SolidColorBrush)view.Resources["Accent"]!).Color);

            // And what does not throw when doubled is doubled silently: one style, not the
            // compiled one with the document's beside it.
            Assert.Single(view.Styles);
            Assert.Equal("Live", Shown(view));
            Assert.DoesNotContain(session.Diagnostics, static d => d.IsError);
        }
    }

    [AvaloniaFact]
    public async Task TheRootsOwnConstructorSeesTheDocumentsObjects()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            View(), Environment(), cancellationToken: TestContext.Current.CancellationToken);

        var view = session.GetRoot<SelfLoadingView>();
        var panel = (StackPanel)view.Content!;

        // What a generated InitializeComponent assigns to the x:Name fields. Populated twice,
        // the constructor kept a control from the markup that was then replaced — so a form's
        // own code went on talking to something that was never on screen.
        Assert.Same(panel.Children[0], view.TitleSeenByTheConstructor);
    }

    [AvaloniaFact]
    public async Task TheRootAndWhatItHoldsAreInTheObjectMap()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            View(), Environment(), cancellationToken: TestContext.Current.CancellationToken);

        var view = session.GetRoot<SelfLoadingView>();
        XamlElement root = Assert.IsType<XamlElement>(session.Document.Root);
        XamlElement title = session.Document.DescendantElements()
            .Single(static element => element.Name.LocalName == "TextBlock");

        // Populating inside the constructor must leave the map exactly what populating after it
        // did: the root paired by assertion, everything below it by where Avalonia built it.
        Assert.Same(view, session.GetObject(root));
        Assert.Same(((StackPanel)view.Content!).Children[0], session.GetObject(title));
    }

    [AvaloniaFact]
    public async Task TheRootIsPopulatedInTheModeTheSessionWasAskedFor()
    {
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            View(rootAttributes: " d:DesignWidth=\"320\""),
            Environment(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        // The population that happens inside the constructor is the session's own, design mode
        // and all — not the run-time one an embedded control gets.
        Assert.Equal(320d, session.GetRoot<SelfLoadingView>().Width);
    }

    [AvaloniaFact]
    public async Task ALiveRegistrationForTheRootsTypeSurvivesTheSession()
    {
        XamlLoadEnvironment environment = Environment();

        using var population = new XamlLivePopulation(environment);

        // What a designer does on opening a form: the document becomes what placed copies of the
        // control are drawn from, and then the form itself is loaded from the same document.
        await population.SetDocumentAsync(
            typeof(SelfLoadingView), View("Registered"), TestContext.Current.CancellationToken);

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            View("Live"), environment, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(session is not null, string.Join(" | ", result.Diagnostics));

        await using (session)
        {
            var view = session.GetRoot<SelfLoadingView>();

            // The session's document, once — not the registered one with the session's on top.
            Assert.Equal("Live", Shown(view));
            Assert.Single(view.Styles);
        }

        // And the registration is still in charge of every instance made afterwards: the session
        // borrowed the hook for one construction and put back what it found there.
        Assert.Equal("Registered", Shown(new SelfLoadingView()));

        population.Dispose();

        Assert.Equal("Compiled", Shown(new SelfLoadingView()));
    }

    [AvaloniaFact]
    public async Task ACopyOfTheRootPlacedInsideItselfIsNotTheRoot()
    {
        XamlLoadEnvironment environment = Environment();

        using var population = new XamlLivePopulation(environment);

        await population.SetDocumentAsync(
            typeof(SelfLoadingView), View("Registered"), TestContext.Current.CancellationToken);

        // A form that places its own control is a cycle only a live document can state. The
        // session's population is for the instance it is building; the copy inside it is an
        // embedded control like any other and is drawn from what is registered for the type.
        await using XamlLoadSession session = await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(
                $"<UserControl xmlns=\"{AvaloniaNamespace}\"\n" +
                $"             xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
                $"             xmlns:tc=\"using:{typeof(SelfLoadingView).Namespace}\"\n" +
                $"             x:Class=\"{typeof(SelfLoadingView).FullName}\">\n" +
                "    <StackPanel>\n" +
                "        <TextBlock x:Name=\"Title\" Text=\"Outer\" />\n" +
                "        <tc:SelfLoadingView />\n" +
                "    </StackPanel>\n" +
                "</UserControl>",
                new XamlParseOptions { DocumentUri = new Uri("file:///Views/SelfLoadingView.axaml") }),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        var view = session.GetRoot<SelfLoadingView>();
        var inner = Assert.IsType<SelfLoadingView>(((StackPanel)view.Content!).Children[1]);

        Assert.Equal("Outer", Shown(view));
        Assert.Equal("Registered", Shown(inner));
    }

    [AvaloniaFact]
    public async Task ADocumentThatWillNotBuildIsReportedAndTheCompiledMarkupStaysInCharge()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            View(content: "        <NoSuchControlAnywhere />\n"),
            Environment(),
            cancellationToken: TestContext.Current.CancellationToken);

        if (session is not null)
        {
            await session.DisposeAsync();
        }

        // Said the way a failed load has always been said, whichever side of the constructor it
        // failed on.
        Assert.Null(session);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.ObjectCreationFailure && d.IsError);
        Assert.Contains(
            result.Diagnostics,
            static d => d.Code == XamlLoaderDiagnosticCodes.NoRootObject);

        // The hook was borrowed and given back, whatever happened in between.
        Assert.Equal("Compiled", Shown(new SelfLoadingView()));
    }
}
