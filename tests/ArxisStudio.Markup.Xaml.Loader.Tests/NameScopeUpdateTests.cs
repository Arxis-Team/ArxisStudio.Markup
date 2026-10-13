using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Names an update writes, takes off or builds again, and the bindings that read an element by its name.
/// </summary>
/// <remarks>
/// A load registers the document's names in a scope it puts on the root and completes, and an update knew nothing of
/// it: a name written on an element was never registered, a binding written in place that read an element by its
/// name had no scope at all, and a part rebuilt on its own was compiled against its own text, where the element it
/// named was not (ADR 0036).
/// </remarks>
public sealed class NameScopeUpdateTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = XamlNamespaces.Xaml;

    private static readonly Uri ViewUri = new("file:///Views/CountedView.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri });

    private static string View(string content, string root = "") =>
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\"\n" +
        $"             x:Class=\"{typeof(CountedView).FullName}\"{root}>\n" +
        content +
        "</UserControl>";

    /// <summary>A slider and a text block beside it, which is what the designer's binding panel writes to.</summary>
    private static string Form(string slider, string text, string root = "", string sliderContent = "") =>
        View(
            "  <StackPanel>\n" +
            (sliderContent.Length == 0
                ? $"    <Slider{slider} Minimum=\"0\" Maximum=\"100\" Value=\"30\" />\n"
                : $"    <Slider{slider} Minimum=\"0\" Maximum=\"100\" Value=\"30\">{sliderContent}</Slider>\n") +
            $"    <TextBlock Text=\"{text}\" />\n" +
            "  </StackPanel>\n",
            root);

    private static async Task<XamlLoadSession> LoadAsync(string xaml)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Parse(xaml),
            XamlLoadEnvironment.CreateDefault([typeof(CountedView).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

        Assert.True(session is not null, "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    private static Task<XamlUpdateResult> UpdateAsync(XamlLoadSession session, string xaml) =>
        session.ApplyDocumentUpdateAsync(Parse(xaml), TestContext.Current.CancellationToken).AsTask();

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    private static StackPanel Panel(XamlLoadSession session) => (StackPanel)session.GetRoot<UserControl>().Content!;

    private static Slider Slider(XamlLoadSession session) => Panel(session).Children.OfType<Slider>().Single();

    private static TextBlock Shown(XamlLoadSession session) => Panel(session).Children.OfType<TextBlock>().First();

    private static object? Named(XamlLoadSession session, string name) =>
        NameScope.GetNameScope(session.GetRoot<UserControl>())?.Find(name);

    /// <summary>Moves the slider on screen and says what the text block shows then.</summary>
    private static string? ShownAfterMoving(XamlLoadSession session, double value)
    {
        Slider(session).Value = value;
        Dispatcher.UIThread.RunJobs();

        return Shown(session).Text;
    }

    [AvaloniaFact]
    public async Task ANameWrittenOnAnElementIsFoundInTheRootsNameScope()
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "0"));

        XamlUpdateResult result = await UpdateAsync(session, Form(" x:Name=\"slider\"", "0"));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(Slider(session), Named(session, "slider"));

        // Completed, as a load leaves one: a name nobody has is answered at once rather than waited for.
        Assert.True(NameScope.GetNameScope(session.GetRoot<UserControl>())!.IsCompleted);
    }

    [AvaloniaFact]
    public async Task ANameWrittenTwiceLeavesTheFirstElementInTheScope()
    {
        string Content(string second) =>
            "  <StackPanel>\n" +
            "    <Slider x:Name=\"slider\" />\n" +
            $"    <TextBlock{second} Text=\"0\" />\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        // The text block is built on its own, where the name is its alone; the root's scope holds one object a name.
        XamlUpdateResult result = await UpdateAsync(session, View(Content(" x:Name=\"slider\"")));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(Slider(session), Named(session, "slider"));
    }

    [AvaloniaFact]
    public async Task ANegatedPathFromAnElementIsWrittenWithTheScope()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "0"));

        XamlUpdateResult result = await UpdateAsync(session, Form(" x:Name=\"slider\"", "{Binding !#slider.IsEnabled}"));

        Assert.True(result.Applied, Describe(result));

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("False", Shown(session).Text);
    }

    [AvaloniaFact]
    public async Task ANameTakenOffAnElementIsGoneFromTheRootsNameScope()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "0"));

        Assert.Same(Slider(session), Named(session, "slider"));

        XamlUpdateResult result = await UpdateAsync(session, Form(string.Empty, "0"));

        Assert.True(result.Applied, Describe(result));
        Assert.Null(Named(session, "slider"));
    }

    [AvaloniaFact]
    public async Task ARenamedElementIsFoundUnderItsNewNameOnly()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "0"));

        XamlUpdateResult result = await UpdateAsync(session, Form(" Name=\"volume\"", "0"));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(Slider(session), Named(session, "volume"));
        Assert.Null(Named(session, "slider"));
    }

    [AvaloniaFact]
    public async Task ANameRewrittenOnAStyledElementIsWrittenByBuildingItAgain()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" Name=\"slider\"", "0"));

        // Shown, so its elements are styled — and Avalonia refuses a name on an element that is.
        var window = new Window { Content = session.GetRoot<UserControl>() };

        window.Show();

        try
        {
            Slider before = Slider(session);

            XamlUpdateResult result = await UpdateAsync(session, Form(" Name=\"volume\"", "0"));

            Assert.True(result.Applied, Describe(result));
            Assert.NotSame(before, Slider(session));
            Assert.Equal("volume", Slider(session).Name);
            Assert.Same(Slider(session), Named(session, "volume"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ANameATemplateUsesIsNotTheDocumentsOwn()
    {
        const string Resources =
            "  <UserControl.Resources>\n" +
            "    <ControlTemplate x:Key=\"Framed\"><Border x:Name=\"caption\" /></ControlTemplate>\n" +
            "  </UserControl.Resources>\n";

        string Content(string slider) =>
            Resources +
            "  <StackPanel>\n" +
            $"    <Slider{slider} />\n" +
            "    <TextBlock x:Name=\"caption\" Text=\"shown\" />\n" +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        XamlUpdateResult result = await UpdateAsync(session, View(Content(" x:Name=\"slider\"")));

        Assert.True(result.Applied, Describe(result));
        Assert.Same(Shown(session), Named(session, "caption"));
    }

    [AvaloniaTheory]
    [InlineData("{Binding #slider.Value}", "", true)]
    [InlineData("{Binding Value, ElementName=slider}", "", true)]
    [InlineData("{Binding #slider.Value}", " x:CompileBindings=\"True\"", false)]
    [InlineData("{Binding Value, ElementName=slider}", " x:CompileBindings=\"True\"", false)]
    public async Task ABindingToAnElementNamedInTheSameUpdateShowsItsValue(string binding, string root, bool inPlace)
    {
        await using XamlLoadSession session = await LoadAsync(Form(string.Empty, "0", root));

        TextBlock before = Shown(session);

        XamlUpdateResult result = await UpdateAsync(session, Form(" x:Name=\"slider\"", binding, root));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(inPlace, ReferenceEquals(before, Shown(session)));

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("30", Shown(session).Text);
        Assert.Equal("70", ShownAfterMoving(session, 70));
    }

    [AvaloniaTheory]
    [InlineData("{Binding #slider.Value}", "", true)]
    [InlineData("{Binding Value, ElementName=slider}", "", true)]
    [InlineData("{Binding #slider.Value}", " x:CompileBindings=\"True\"", false)]
    [InlineData("{Binding Value, ElementName=slider}", " x:CompileBindings=\"True\"", false)]
    public async Task ABindingWrittenToAnElementNamedAtTheLoadShowsItsValue(string binding, string root, bool inPlace)
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "0", root));

        TextBlock before = Shown(session);

        XamlUpdateResult result = await UpdateAsync(session, Form(" x:Name=\"slider\"", binding, root));

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(inPlace, ReferenceEquals(before, Shown(session)));

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("30", Shown(session).Text);
        Assert.Equal("70", ShownAfterMoving(session, 70));
    }

    [AvaloniaFact]
    public async Task ABindingWrittenAsAnElementIsBuiltWithTheElementItNames()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "0", " x:CompileBindings=\"True\""));

        // The text block gains a property element, so only its own object can take the value — and its binding, built
        // with it, names an element outside it.
        string updated = Form(" x:Name=\"slider\"", "0", " x:CompileBindings=\"True\"").Replace(
            "<TextBlock Text=\"0\" />",
            "<TextBlock><TextBlock.Text><Binding ElementName=\"slider\" Path=\"Value\" /></TextBlock.Text></TextBlock>",
            StringComparison.Ordinal);

        XamlUpdateResult result = await UpdateAsync(session, updated);

        Assert.True(result.Applied, Describe(result));

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("30", Shown(session).Text);
        Assert.Equal("70", ShownAfterMoving(session, 70));
    }

    [AvaloniaFact]
    public async Task ABindingFollowsANamedElementTheUpdateBuildsAgain()
    {
        await using XamlLoadSession session = await LoadAsync(Form(" x:Name=\"slider\"", "{Binding #slider.Value}"));

        Slider before = Slider(session);

        // A member written as a property element that only a new slider can take.
        XamlUpdateResult result = await UpdateAsync(
            session,
            Form(" x:Name=\"slider\"", "{Binding #slider.Value}", sliderContent: "<ToolTip.Tip>Volume</ToolTip.Tip>"));

        Assert.True(result.Applied, Describe(result));
        Assert.NotSame(before, Slider(session));
        Assert.Same(Slider(session), Named(session, "slider"));
        Assert.Equal("70", ShownAfterMoving(session, 70));
    }

    [AvaloniaFact]
    public async Task ABindingInsideANamedPanelReadsThePanelAfterThePanelsContentIsRebuilt()
    {
        string Content(string added) =>
            "  <StackPanel x:Name=\"panel\" Orientation=\"Vertical\">\n" +
            "    <TextBlock Text=\"{Binding #panel.Orientation}\" />\n" +
            added +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        XamlUpdateResult result = await UpdateAsync(session, View(Content("    <TextBlock Text=\"added\" />\n")));

        Assert.True(result.Applied, Describe(result));

        // The panel on screen, not the copy its content was built in.
        Panel(session).Orientation = Orientation.Horizontal;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Horizontal", Shown(session).Text);
        Assert.Same(Panel(session), Named(session, "panel"));
    }

    [AvaloniaFact]
    public async Task APanelThatReadsAnElementInsideItFollowsItWhenItsContentIsRebuilt()
    {
        string Content(string added) =>
            "  <StackPanel Tag=\"{Binding #inner.Text}\">\n" +
            "    <TextBlock x:Name=\"inner\" Text=\"inner\" />\n" +
            added +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty)));

        XamlUpdateResult result = await UpdateAsync(session, View(Content("    <TextBlock Text=\"added\" />\n")));

        Assert.True(result.Applied, Describe(result));

        // The text block on screen is a new one, and the panel's own binding — which a rebuild of only what the
        // panel holds would keep — reads it.
        Shown(session).Text = "changed";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("changed", Panel(session).Tag);
    }

    [AvaloniaFact]
    public async Task APartThatReadsTheRootByItsNameIsANewSession()
    {
        string Content(string added) =>
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"{Binding #Root.Tag}\" />\n" +
            added +
            "  </StackPanel>\n";

        await using XamlLoadSession session = await LoadAsync(View(Content(string.Empty), " x:Name=\"Root\" Tag=\"tagged\""));

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("tagged", Shown(session).Text);

        // Only the root holds both the text block and the element it names, and the root is what a session is built
        // around: a part built without it would read a copy, or nothing.
        XamlUpdateResult result = await UpdateAsync(
            session, View(Content("    <TextBlock Text=\"added\" />\n"), " x:Name=\"Root\" Tag=\"tagged\""));

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Equal("tagged", Shown(session).Text);
    }
}
