using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// A control with markup of its own, placed on a form, is the form's element — and what its own
/// markup built inside it is not.
/// </summary>
/// <remarks>
/// The map pairs an object with an element by where Avalonia recorded building it, and a placed
/// control answers with its own document: its constructor loads its compiled markup over the
/// instance. The map was right to refuse that answer, and the refusal left the control mapped to
/// nothing — a click on it selected the form, and deleting it had nothing to delete.
/// </remarks>
public sealed class PlacedControlTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static readonly Uri FormUri = new("file:///Views/Form.axaml");

    private static XamlDocument Parse(string xaml) =>
        XamlDocument.Parse(xaml, new XamlParseOptions { DocumentUri = FormUri });

    private static string Form(string children) =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:tc=\"{ControlsNamespace}\">\n" +
        children +
        "</StackPanel>";

    private static ValueTask<XamlLoadSession> Load(string xaml) =>
        XamlLoadSession.CreateAsync(
            Parse(xaml),
            XamlLoadEnvironment.CreateDefault([typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider()),
            new XamlLoadOptions { Mode = XamlLoadMode.Design },
            TestContext.Current.CancellationToken);

    private const string Placed =
        "  <TextBlock Text=\"before\" />\n" +
        "  <tc:LiveControl />\n" +
        "  <TextBlock Text=\"after\" />\n";

    [AvaloniaFact]
    public async Task APlacedXClassControlIsMappedToItsElement()
    {
        await using XamlLoadSession session = await Load(Form(Placed));

        LiveControl placed = session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single();
        XamlElement? element = session.GetElement(placed);

        Assert.NotNull(element);
        Assert.Equal("LiveControl", element.Name.LocalName);
        Assert.Same(placed, session.GetObject(element));
        Assert.Equal(XamlObjectOrigin.Document, session.GetOrigin(placed));
        Assert.Equal(FormUri, session.GetSourceUri(placed));
    }

    [AvaloniaFact]
    public async Task ThePlacedControlsInternalsStayUnmapped()
    {
        await using XamlLoadSession session = await Load(Form(Placed));

        LiveControl placed = session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single();

        TextBlock inner = placed.GetLogicalDescendants().OfType<TextBlock>().Single();

        Assert.Equal("Compiled", inner.Text);
        Assert.Null(session.GetElement(inner));
        Assert.NotEqual(XamlObjectOrigin.Document, session.GetOrigin(inner));

        // And the siblings it sits between keep their own elements.
        TextBlock after = session.GetRoot<StackPanel>().Children.OfType<TextBlock>().Last();

        Assert.Equal("after", session.GetElement(after)?.GetAttribute("Text")?.GetValueText());
    }

    [AvaloniaFact]
    public async Task AControlPopulatedFromItsLiveDocumentIsMappedToTheFormsElement()
    {
        // The arrangement a designer runs in: the control's own markup is open too, and live
        // population builds every instance from it — so Avalonia records the control's document,
        // not the form's, on the instance the form placed.
        using var population = new XamlLivePopulation(
            XamlLoadEnvironment.CreateDefault([typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider()));

        XamlLivePopulationResult installed = await population.SetDocumentAsync(
            typeof(LiveControl),
            XamlDocument.Parse(
                $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
                $"             x:Class=\"{typeof(LiveControl).FullName}\">\n" +
                "  <TextBlock Text=\"Live\" />\n" +
                "</UserControl>",
                new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") }),
            TestContext.Current.CancellationToken);

        Assert.True(installed.Success, string.Join(" | ", installed.Diagnostics));

        await using XamlLoadSession session = await Load(Form(Placed));

        LiveControl placed = session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single();

        Assert.Equal("Live", Assert.IsType<TextBlock>(placed.Content).Text);

        XamlElement? element = session.GetElement(placed);

        Assert.NotNull(element);
        Assert.Equal("LiveControl", element.Name.LocalName);
        Assert.Null(session.GetElement((TextBlock)placed.Content!));
    }

    [AvaloniaFact]
    public async Task APlacedControlRebuiltOnItsOwnIsPopulatedByItsOwnMarkup()
    {
        using var population = new XamlLivePopulation(
            XamlLoadEnvironment.CreateDefault([typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider()));

        Assert.True((await population.SetDocumentAsync(
            typeof(LiveControl),
            XamlDocument.Parse(
                $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
                $"             x:Class=\"{typeof(LiveControl).FullName}\">\n" +
                "  <TextBlock Text=\"Live\" />\n" +
                "</UserControl>",
                new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") }),
            TestContext.Current.CancellationToken)).Success);

        string Xaml(string attributes) =>
            $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:tc=\"{ControlsNamespace}\">\n" +
            $"  <tc:LiveControl{attributes} />\n" +
            "</StackPanel>";

        await using XamlLoadSession session = await Load(Xaml(string.Empty));

        // An expression nothing writes in place rebuilds the control, and the control is then the
        // root of the part Avalonia is given — which it takes for the control's own definition.
        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(Xaml(" Tag=\"{x:Type TextBlock}\"")), TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);

        LiveControl placed = session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single();

        Assert.Equal(typeof(TextBlock), placed.Tag);
        Assert.Equal("Live", Assert.IsType<TextBlock>(placed.Content).Text);

        // And the registration still answers for every instance made after the rebuild.
        Assert.Equal("Live", Assert.IsType<TextBlock>(new LiveControl().Content).Text);
    }

    [AvaloniaFact]
    public async Task DeletingAPlacedControlAppliesInPlace()
    {
        await using XamlLoadSession session = await Load(Form(Placed));

        object root = session.RootObject;

        XamlUpdateResult result = await session.ApplyDocumentUpdateAsync(
            Parse(Form("  <TextBlock Text=\"before\" />\n  <TextBlock Text=\"after\" />\n")),
            TestContext.Current.CancellationToken);

        Assert.True(result.Applied, string.Join(" | ", result.Diagnostics));
        Assert.Same(root, session.RootObject);
        Assert.Empty(session.GetRoot<StackPanel>().Children.OfType<LiveControl>());
        Assert.Equal(2, session.GetRoot<StackPanel>().Children.Count);
    }
}
