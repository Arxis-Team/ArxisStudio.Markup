using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Updates of a document whose <c>x:Class</c> resolved — the form a designer shows beside the IDE
/// that edits it.
/// </summary>
/// <remarks>
/// The update path was written around documents whose class the load could not use. With the class
/// there, rebuilding the root's content constructed it a second time, a part holding a handler the
/// class declares could not be rebuilt at all, and a part under <c>x:DataType</c> lost the type its
/// compiled bindings need.
/// </remarks>
public sealed class ClassKeepingUpdateTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri ViewUri = new("file:///Views/CountedView.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider());

    private static string View(string content, string attributes = "") =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"             x:Class=\"{typeof(CountedView).FullName}\"{attributes}>\n" +
        content +
        "</UserControl>";

    private static string Window(string content) =>
        $"<tc:TrackedWindow xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\" xmlns:tc=\"{ControlsNamespace}\"\n" +
        $"                  x:Class=\"{typeof(CountedWindow).FullName}\" Title=\"Counted\">\n" +
        content +
        "</tc:TrackedWindow>";

    private static async Task<XamlLoadSession> LoadAsync(string xaml, XamlLoadOptions? options = null)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            Environment(),
            options ?? new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(
            session is not null,
            "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    private static async Task<XamlUpdateResult> UpdateAsync(XamlLoadSession session, string xaml) =>
        await session.ApplyDocumentUpdateAsync(Parse(xaml), TestContext.Current.CancellationToken);

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task RebuildingTheRootsContentDoesNotConstructTheClassAgain()
    {
        const string Content =
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"one\" />\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content));
        object root = session.RootObject;
        int constructed = CountedView.Constructed;

        // A property element added to the root is a change to what the root holds, which is
        // rebuilt from the root's own markup — the one projection that used to carry x:Class.
        XamlUpdateResult result = await UpdateAsync(
            session,
            View(
                "  <UserControl.Resources>\n" +
                "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
                "  </UserControl.Resources>\n" +
                Content));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(root, session.RootObject);
        Assert.Equal(constructed, CountedView.Constructed);
        Assert.True(session.GetRoot<UserControl>().Resources.ContainsKey("Accent"));
    }

    [AvaloniaFact]
    public async Task RebuildingAWindowsContentLeavesNoWindowOpen()
    {
        const string Content = "  <StackPanel />\n";

        XamlLoadSession session = await LoadAsync(Window(Content));
        var root = session.GetRoot<CountedWindow>();

        try
        {
            HashSet<TrackedWindow> before = [.. TrackedWindow.Open()];
            int constructed = CountedWindow.Constructed;

            XamlUpdateResult result = await UpdateAsync(
                session,
                Window(
                    "  <tc:TrackedWindow.Resources>\n" +
                    "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
                    "  </tc:TrackedWindow.Resources>\n" +
                    Content));

            Assert.True(result.Applied, Describe(result));
            Assert.Equal(constructed, CountedWindow.Constructed);

            TrackedWindow[] leftOpen = [.. TrackedWindow.Open().Where(window => !before.Contains(window))];

            Assert.True(
                leftOpen.Length == 0,
                $"The rebuild left {leftOpen.Length} window(s) open: "
                    + string.Join(", ", leftOpen.Select(static window => window.GetType().Name)));
        }
        finally
        {
            await session.DisposeAsync();
            root.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("  <tc:TrackedWindow.Background>\n    <SolidColorBrush Color=\"Red\" />\n  </tc:TrackedWindow.Background>\n")]
    [InlineData("  <Design.DataContext>\n    <tc:GreetingModel />\n  </Design.DataContext>\n")]
    public async Task ARootTheUpdateCannotRebuildLeavesNoWindowOpen(string added)
    {
        const string Content = "  <StackPanel />\n";

        XamlLoadSession session = await LoadAsync(Window(Content));
        var root = session.GetRoot<CountedWindow>();

        try
        {
            HashSet<TrackedWindow> before = [.. TrackedWindow.Open()];

            // A single value written as a property element of the root cannot be moved onto it from a
            // copy, which the update finds out only once it has built the copy; it then asks for a new
            // session — and the copy, a window like the root, is still the update's to close.
            XamlUpdateResult result = await UpdateAsync(session, Window(added + Content));

            Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);

            TrackedWindow[] leftOpen = [.. TrackedWindow.Open().Where(window => !before.Contains(window))];

            Assert.True(
                leftOpen.Length == 0,
                $"The refused update left {leftOpen.Length} window(s) open: "
                    + string.Join(", ", leftOpen.Select(static window => window.GetType().Name)));
        }
        finally
        {
            await session.DisposeAsync();
            root.Close();
        }
    }

    [AvaloniaFact]
    public async Task AStructuralChangeInsideAPanelHoldingAHandlerAppliesInPlace()
    {
        await using XamlLoadSession session = await LoadAsync(View(
            "  <StackPanel>\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />\n" +
            "  </StackPanel>\n"));

        object root = session.RootObject;

        XamlUpdateResult result = await UpdateAsync(session, View(
            "  <StackPanel>\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />\n" +
            "    <TextBlock Text=\"added\" />\n" +
            "  </StackPanel>\n"));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(root, session.RootObject);
        Assert.Equal(2, Assert.IsType<StackPanel>(session.GetRoot<UserControl>().Content).Children.Count);
    }

    [AvaloniaFact]
    public async Task ARebuiltButtonsHandlerRunsOnTheRootInstance()
    {
        await using XamlLoadSession session = await LoadAsync(View(
            "  <StackPanel>\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />\n" +
            "  </StackPanel>\n"));

        XamlUpdateResult result = await UpdateAsync(session, View(
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"before\" />\n" +
            "    <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />\n" +
            "  </StackPanel>\n"));

        Assert.True(result.Applied, Describe(result));

        var view = session.GetRoot<CountedView>();
        var panel = Assert.IsType<StackPanel>(view.Content);
        Button button = panel.Children.OfType<Button>().Single();

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, view.SaveClickCount);
        Assert.Same(button, view.LastSender);
    }

    [AvaloniaFact]
    public async Task AHandlerInARebuildInsideAnotherRunsOnce()
    {
        string Content(string reads, string added) =>
            "  <UserControl.Resources>\n" +
            "    <SolidColorBrush x:Key=\"Accent\" Color=\"Red\" />\n" +
            "  </UserControl.Resources>\n" +
            "  <StackPanel>\n" +
            $"    <Border{reads} />\n" +
            "    <StackPanel>\n" +
            $"      <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\" />{added}\n" +
            "    </StackPanel>\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty, string.Empty)));

        // The inner panel gained a child, which rebuilds the panel; the border now reads a key the
        // root declares, which rebuilds what the root holds — the panel and its button with it.
        XamlUpdateResult result = await UpdateAsync(
            session,
            View(Content(" Background=\"{StaticResource Accent}\"", "<TextBlock Text=\"added\" />")));

        Assert.True(result.Applied, Describe(result));

        var view = session.GetRoot<CountedView>();
        var inner = (StackPanel)((StackPanel)view.Content!).Children[1];
        Button button = inner.Children.OfType<Button>().Single();
        int clicks = view.SaveClickCount;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(clicks + 1, view.SaveClickCount);
    }

    [AvaloniaFact]
    public async Task AHandlerOnAnElementWhoseContentIsRebuiltRunsOnce()
    {
        string Content(string resources) =>
            "  <StackPanel>\n" +
            $"    <Button x:Name=\"Save\" Content=\"Save\" Click=\"SaveClicked\">{resources}</Button>\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        var view = session.GetRoot<CountedView>();
        Button before = ((StackPanel)view.Content!).Children.OfType<Button>().Single();

        // A dictionary added to the button changes what the button holds: its content is rebuilt
        // from a copy, and the button itself stays — with the handler the load hooked up to it.
        XamlUpdateResult result = await UpdateAsync(
            session,
            View(Content("<Button.Resources><SolidColorBrush x:Key=\"Accent\" Color=\"Red\" /></Button.Resources>")));

        Assert.True(result.Applied, Describe(result));

        Button button = ((StackPanel)view.Content!).Children.OfType<Button>().Single();
        int clicks = view.SaveClickCount;

        Assert.Same(before, button);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(clicks + 1, view.SaveClickCount);
    }

    [AvaloniaFact]
    public async Task AHandlerAddedToAChildIsHookedUpWhenTheChildIsRebuilt()
    {
        string Content(string handler) =>
            "  <StackPanel>\n" +
            $"    <Button x:Name=\"Save\" Content=\"Save\"{handler} />\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        // A handler is not a value to set: it is hooked up when its element is built.
        XamlUpdateResult result = await UpdateAsync(session, View(Content(" Click=\"SaveClicked\"")));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);

        var view = session.GetRoot<CountedView>();
        Button button = ((StackPanel)view.Content!).Children.OfType<Button>().Single();
        int clicks = view.SaveClickCount;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(clicks + 1, view.SaveClickCount);
    }

    [AvaloniaFact]
    public async Task AHandlerAddedToTheRootNeedsANewSession()
    {
        await using XamlLoadSession session = await LoadAsync(View("  <StackPanel />\n"));

        object root = session.RootObject;

        XamlUpdateResult result = await UpdateAsync(session, View("  <StackPanel />\n", " Loaded=\"SaveClicked\""));

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Same(root, session.RootObject);
    }

    [AvaloniaFact]
    public async Task APrivateHandlerOfTheClassIsHookedUpByTheLoadAndByARebuild()
    {
        string Xaml(string added) =>
            $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
            $"             x:Class=\"{typeof(PrivateHandlerView).FullName}\">\n" +
            "  <StackPanel>\n" +
            $"    <Button x:Name=\"Save\" Content=\"Save\" Click=\"Clicked\" />{added}\n" +
            "  </StackPanel>\n" +
            "</UserControl>";

        // No local assembly named: the class the document populates says which assembly it is.
        (XamlLoadSession? created, XamlLoadResult loaded) = await XamlLoadSession.TryCreateAsync(
            Parse(Xaml(string.Empty)),
            XamlLoadEnvironment.CreateDefault(
                [typeof(PrivateHandlerView).Assembly, typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(created is not null, "The document produced no object: " + string.Join(" | ", loaded.Diagnostics));

        await using XamlLoadSession session = created;

        var view = session.GetRoot<PrivateHandlerView>();
        Button Save() => ((StackPanel)view.Content!).Children.OfType<Button>().Single();

        Save().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, view.Clicks);

        XamlUpdateResult result = await UpdateAsync(session, Xaml("<TextBlock Text=\"added\" />"));

        Assert.True(result.Applied, Describe(result));

        Save().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(2, view.Clicks);
    }

    [AvaloniaFact]
    public async Task AHandlerWithTheWrongSignatureIsReportedAndTheUpdateStillApplies()
    {
        await using XamlLoadSession session = await LoadAsync(View(
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"one\" />\n" +
            "  </StackPanel>\n"));

        XamlUpdateResult result = await UpdateAsync(session, View(
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"one\" />\n" +
            "    <Button Content=\"Count\" Click=\"Miscounted\" />\n" +
            "  </StackPanel>\n"));

        Assert.True(result.Applied, Describe(result));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.HandlerSignatureMismatch
                && diagnostic.Severity == MarkupDiagnosticSeverity.Warning);

        var panel = Assert.IsType<StackPanel>(session.GetRoot<UserControl>().Content);

        Assert.Equal(2, panel.Children.Count);
    }

    [AvaloniaFact]
    public async Task AStructuralChangeUnderCompiledBindingsCarriesTheDataType()
    {
        var options = new XamlLoadOptions { Mode = XamlLoadMode.Design, UseCompiledBindingsByDefault = true };

        await using XamlLoadSession session = await LoadAsync(
            View(
                "  <StackPanel>\n" +
                "    <TextBlock Text=\"{Binding Name}\" />\n" +
                "  </StackPanel>\n",
                " x:DataType=\"tc:GreetingModel\""),
            options);

        XamlUpdateResult result = await UpdateAsync(
            session,
            View(
                "  <StackPanel>\n" +
                "    <TextBlock Text=\"{Binding Name}\" />\n" +
                "    <TextBlock Text=\"{Binding Name}\" />\n" +
                "  </StackPanel>\n",
                " x:DataType=\"tc:GreetingModel\""));

        Assert.True(result.Applied, Describe(result));

        var view = session.GetRoot<UserControl>();
        view.DataContext = new GreetingModel { Name = "Grace" };
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<StackPanel>(view.Content);

        Assert.All(
            panel.Children.OfType<TextBlock>(),
            static text => Assert.Equal("Grace", text.Text));
    }
}

/// <summary>
/// A view whose handler is private, which is how code-behind usually writes one.
/// </summary>
/// <remarks>
/// <para>
/// A runtime load compiles the document into an assembly of its own, which calls a private method of
/// the class only when the load names the class's assembly as the one the document belongs to.
/// </para>
/// <para>
/// Here rather than among the test controls, and on purpose: Avalonia's runtime compiler keeps the
/// assemblies it has been told to reach for the life of the process, and live population names the
/// test controls' assembly in the same run — a fixture there passed whether or not the session named
/// anything. No other test names this assembly.
/// </para>
/// </remarks>
public class PrivateHandlerView : UserControl
{
    /// <summary>Gets how many times the private handler has run on this view.</summary>
    public int Clicks { get; private set; }

    private void Clicked(object? sender, RoutedEventArgs e) => Clicks++;
}
