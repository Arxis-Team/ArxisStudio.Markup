using System;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Design.Tests;

/// <summary>
/// Standing in for a root that cannot be shown.
/// </summary>
/// <remarks>
/// Two of these are about the premise rather than the code: a window really cannot be hosted, and
/// its content really does lose the window's resources when taken out. Both were found by
/// experiment while building a designer, and both are cheap to assert and expensive to rediscover.
/// </remarks>
public sealed class XamlDesignSurfaceTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static string Form(
        string attributes = "",
        string declarations = "",
        string content = "<TextBlock x:Name=\"Text\" Text=\"hello\" />") =>
        $"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" {attributes}>\n" +
        declarations +
        content +
        "\n</Window>";

    private static async Task<XamlLoadSession> LoadAsync(string xaml) =>
        await XamlLoadSession.CreateAsync(
            XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = ViewUri }),
            XamlLoadEnvironment.CreateDefault(),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Puts a surface in a live tree, because styles and resources need somewhere to run.
    /// </summary>
    /// <remarks>
    /// The host window is not closed and not returned. A headless test application goes away with
    /// the test, and closing it here would tear down the tree the assertion is about.
    /// </remarks>
    private static void Host(XamlDesignSurface surface)
    {
        var host = new Window { Content = surface, Width = 900, Height = 600 };

        host.Show();
        host.UpdateLayout();
    }

    private static T Find<T>(XamlLoadSession session, string name)
        where T : Control =>
        Assert.IsType<T>(session.GetRoot<Window>().FindControl<Control>(name));

    // -- The premise ------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AWindow_CannotBeHostedByAnything_WhichIsWhyThisTypeExists()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        var window = session.GetRoot<Window>();
        var border = new Border();

        Assert.ThrowsAny<InvalidOperationException>(() =>
        {
            border.Child = window;
            border.Measure(Size.Infinity);
        });
    }

    // -- Hosting ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Attach_HostsTheContentOfATopLevelRoot()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.True(surface.IsTopLevel);
        Assert.True(surface.HasContent);
        Assert.Same(session.RootObject, surface.Root);

        Host(surface);

        Assert.True(Find<TextBlock>(session, "Text").Bounds.Width > 0);
    }

    [AvaloniaFact]
    public async Task Attach_HostsANonTopLevelRootAsItStands()
    {
        await using XamlLoadSession session = await LoadAsync(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><TextBlock x:Name=\"Text\" /></UserControl>");

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.False(surface.IsTopLevel);
        Assert.True(surface.HasContent);
        Assert.Same(session.RootObject, surface.Root);
    }

    [AvaloniaFact]
    public async Task Attach_ReportsNoContent_WhenTheDocumentProducedSomethingThatIsNotAControl()
    {
        await using XamlLoadSession session = await LoadAsync(
            $"<ResourceDictionary xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" />");

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.False(surface.HasContent);
        Assert.False(surface.IsTopLevel);
        Assert.Same(session.RootObject, surface.Root);
    }

    // -- What the content would otherwise lose ----------------------------------------------------

    [AvaloniaFact]
    public async Task DetachedContent_LosesTheWindowsResources_WithoutASurface()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Resources><SolidColorBrush x:Key=\"Accent\">#FF0000</SolidColorBrush></Window.Resources>"));

        var window = session.GetRoot<Window>();
        var text = Assert.IsType<TextBlock>(window.Content);

        Assert.True(text.TryFindResource("Accent", out _));

        window.Content = null;

        Assert.False(text.TryFindResource("Accent", out _));
    }

    [AvaloniaFact]
    public async Task Attach_SharesTheRootsResources_SoTheContentStillFindsThem()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Resources><SolidColorBrush x:Key=\"Accent\">#FF0000</SolidColorBrush></Window.Resources>"));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Host(surface);

        Assert.True(Find<TextBlock>(session, "Text").TryFindResource("Accent", out _));
    }

    [AvaloniaFact]
    public async Task Attach_SharesTheRootsStyles_SoAStyleDeclaredOnTheRootStillApplies()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Styles><Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"42\" /></Style></Window.Styles>"));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Host(surface);

        Assert.Equal(42d, Find<TextBlock>(session, "Text").FontSize);
    }

    // -- Projection, not a snapshot ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task Attach_MirrorsTheBackground()
    {
        await using XamlLoadSession session = await LoadAsync(Form("Background=\"#FF0000\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
    }

    /// <summary>
    /// A background the document did not ask for is not the form's background.
    /// </summary>
    /// <remarks>
    /// A window always ends up with one — the application hosting the designer supplies a themed
    /// default — so binding straight through would paint every undecided form in the tool's own
    /// colour and claim it was the form's. The priority used here is what a theme uses.
    /// </remarks>
    [AvaloniaFact]
    public async Task AThemedDefaultBackground_IsNotShown_ButALocalOneIs()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        var window = session.GetRoot<Window>();

        window.SetValue(TemplatedControl.BackgroundProperty, Brushes.Black, BindingPriority.Style);

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Null(surface.Background);

        window.SetValue(TemplatedControl.BackgroundProperty, Brushes.Lime);

        Assert.Equal(Colors.Lime, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
    }

    [AvaloniaFact]
    public async Task AnEditToTheRoot_ShowsOnTheSurface_WithoutReattaching()
    {
        await using XamlLoadSession session = await LoadAsync(Form("Background=\"#FF0000\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        XamlEditResult edit = session.SetValue(
            session.GetRoot<Window>(), TemplatedControl.BackgroundProperty, Brushes.Lime);

        Assert.True(edit.Applied, string.Join(" | ", edit.Diagnostics));
        Assert.Equal(Colors.Lime, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
    }

    [AvaloniaFact]
    public async Task Attach_MovesTheRootsResources_AndDetachGivesThemBack()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Resources><SolidColorBrush x:Key=\"Accent\">#FF0000</SolidColorBrush></Window.Resources>"));

        var window = session.GetRoot<Window>();

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        // Moved rather than shared, because Avalonia allows a dictionary one owner. While the
        // stand-in holds it the root has an empty one, and the document is what still says
        // otherwise -- which is what every edit path reads.
        Assert.True(surface.TryFindResource("Accent", out _));

        surface.Detach();

        Assert.True(window.TryFindResource("Accent", out _));
        Assert.False(surface.TryFindResource("Accent", out _));
    }

    /// <summary>
    /// The reason resources are moved rather than copied.
    /// </summary>
    /// <remarks>
    /// A copy can only reach the entries it can enumerate, and a merged dictionary is a separate
    /// object with an owner of its own. Copying therefore flattens away exactly the structure a
    /// form with a shared palette depends on, and does it silently. Moving keeps the same object,
    /// so there is nothing to flatten.
    /// </remarks>
    [AvaloniaFact]
    public async Task Attach_KeepsMergedDictionariesIntact()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
            "<ResourceDictionary><SolidColorBrush x:Key=\"Paper\">#000000</SolidColorBrush></ResourceDictionary>" +
            "</ResourceDictionary.MergedDictionaries></ResourceDictionary></Window.Resources>"));

        var text = Find<TextBlock>(session, "Text");

        Assert.True(text.TryFindResource("Paper", out _), "the window itself does not resolve it");

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Host(surface);

        Assert.True(text.TryFindResource("Paper", out object? paper), "the stand-in does not resolve it");
        Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(paper).Color);
    }

    [AvaloniaFact]
    public async Task Attach_MovesTheRootsStyles_AndDetachGivesThemBack()
    {
        await using XamlLoadSession session = await LoadAsync(Form(
            declarations:
            "<Window.Styles><Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"42\" /></Style></Window.Styles>"));

        var window = session.GetRoot<Window>();

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Single(surface.Styles);
        Assert.Empty(window.Styles);

        surface.Detach();

        Assert.Empty(surface.Styles);
        Assert.Single(window.Styles);
    }

    [AvaloniaFact]
    public async Task Attach_MirrorsTheDeclaredSize()
    {
        await using XamlLoadSession session = await LoadAsync(Form("Width=\"800\" Height=\"450\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Equal(800d, surface.Width);
        Assert.Equal(450d, surface.Height);
    }

    [AvaloniaFact]
    public async Task Attach_CarriesTheRequestedThemeVariant()
    {
        await using XamlLoadSession session = await LoadAsync(Form("RequestedThemeVariant=\"Light\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Host(surface);

        Assert.Equal(ThemeVariant.Light, Find<TextBlock>(session, "Text").ActualThemeVariant);
    }

    // -- The context the content would otherwise lose, or inherit wrongly -------------------------

    [AvaloniaFact]
    public async Task Attach_CarriesTheRootsDataContext()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        var window = session.GetRoot<Window>();

        window.DataContext = "the form's own";

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Equal("the form's own", Find<TextBlock>(session, "Text").DataContext);
    }

    /// <summary>
    /// A form must not be shown the host's data.
    /// </summary>
    /// <remarks>
    /// The stand-in usually sits in a template bound to whatever the host is showing, and a data
    /// context inherits down the tree it is in. Without a local value here, a form that declares no
    /// design-time data of its own would quietly render against the designer's view model, and the
    /// bindings that happened to match would look like they were working.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheHostsDataContext_DoesNotReachTheForm()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        using var surface = new XamlDesignSurface();

        surface.Attach(session);
        surface.DataContext = "the designer's";

        Host(surface);

        Assert.Null(Find<TextBlock>(session, "Text").DataContext);
    }

    [AvaloniaFact]
    public async Task TheHostsDataContext_DoesNotReachANonTopLevelRootEither()
    {
        await using XamlLoadSession session = await LoadAsync(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><TextBlock x:Name=\"Text\" /></UserControl>");

        using var surface = new XamlDesignSurface();

        surface.Attach(session);
        surface.DataContext = "the designer's";

        Host(surface);

        Assert.Null(session.GetRoot<UserControl>().DataContext);
    }

    // -- Chrome is data ---------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Attach_PublishesTheWindowsChrome()
    {
        await using XamlLoadSession session = await LoadAsync(
            Form("Title=\"Orders\" CanResize=\"False\" WindowDecorations=\"BorderOnly\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Equal("Orders", surface.Title);
        Assert.False(surface.CanResize);
        Assert.Equal(WindowDecorations.BorderOnly, surface.Decorations);
    }

    [AvaloniaFact]
    public async Task ANonTopLevelRoot_PublishesNoChrome()
    {
        await using XamlLoadSession session = await LoadAsync(
            $"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\"><TextBlock /></UserControl>");

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Null(surface.Title);
    }

    // -- One writer -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheSurface_NeverWritesBackToTheRoot()
    {
        await using XamlLoadSession session = await LoadAsync(Form("Width=\"800\" Background=\"#FF0000\""));

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        surface.Width = 123;
        surface.Background = Brushes.Blue;

        var window = session.GetRoot<Window>();

        Assert.Equal(800d, window.Width);
        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
    }

    // -- Lifetime ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Detach_GivesTheContentBack()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        var window = session.GetRoot<Window>();
        object? content = window.Content;

        using var surface = new XamlDesignSurface();

        surface.Attach(session);

        Assert.Null(window.Content);

        surface.Detach();

        Assert.Same(content, window.Content);
        Assert.Null(surface.Root);
        Assert.False(surface.HasContent);
    }

    [AvaloniaFact]
    public async Task Attach_Twice_ReleasesTheFirstRoot()
    {
        await using XamlLoadSession first = await LoadAsync(Form("Title=\"First\""));
        await using XamlLoadSession second = await LoadAsync(Form("Title=\"Second\""));

        var firstWindow = first.GetRoot<Window>();
        object? firstContent = firstWindow.Content;

        using var surface = new XamlDesignSurface();

        surface.Attach(first);
        surface.Attach(second);

        Assert.Same(firstContent, firstWindow.Content);
        Assert.Same(second.RootObject, surface.Root);
        Assert.Equal("Second", surface.Title);
    }

    [AvaloniaFact]
    public async Task Dispose_DetachesAndRefusesToAttachAgain()
    {
        await using XamlLoadSession session = await LoadAsync(Form());

        var window = session.GetRoot<Window>();
        object? content = window.Content;

        var surface = new XamlDesignSurface();

        surface.Attach(session);
        surface.Dispose();

        Assert.Same(content, window.Content);
        Assert.Throws<ObjectDisposedException>(() => surface.Attach(session));
    }

    [AvaloniaFact]
    public void Detach_DoesNothing_WhenNothingIsAttached()
    {
        using var surface = new XamlDesignSurface();

        surface.Detach();
        surface.Detach();

        Assert.Null(surface.Root);
    }

    [AvaloniaFact]
    public void Attach_RejectsNull()
    {
        using var surface = new XamlDesignSurface();

        Assert.Throws<ArgumentNullException>(() => surface.Attach(null!));
    }
}
