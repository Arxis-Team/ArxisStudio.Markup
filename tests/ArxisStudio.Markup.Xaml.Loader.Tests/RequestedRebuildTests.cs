using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Elements built again at a host's request, because the markup of the control they place changed
/// while the document that places it did not.
/// </summary>
public sealed class RequestedRebuildTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "using:ArxisStudio.Markup.Xaml.Loader.TestControls";

    private static readonly Uri FormUri = new("file:///Views/Form.axaml");

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider());

    private static XamlDocument Form(string placed) => XamlDocument.Parse(
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:tc=\"{ControlsNamespace}\">\n" +
        "  <TextBlock Text=\"kept\" />\n" +
        $"  {placed}\n" +
        "</StackPanel>",
        new XamlParseOptions { DocumentUri = FormUri });

    private static XamlDocument LiveControlDocument(string text) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             x:Class=\"{typeof(LiveControl).FullName}\">\n" +
        $"  <TextBlock Text=\"{text}\" />\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") });

    private static async Task<XamlLoadSession> LoadAsync(XamlDocument document)
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, Environment(), new XamlLoadOptions(), TestContext.Current.CancellationToken);

        Assert.True(session is not null, "The document produced no object: " + string.Join(" | ", result.Diagnostics));

        return session;
    }

    private static XamlElement Named(XamlLoadSession session, string name) =>
        session.Document.DescendantElements().Single(element => element.GetDirective("Name") == name);

    private static string Describe(XamlUpdateResult result) =>
        $"{result.Outcome} / {result.Strategy}: " + string.Join(" | ", result.Diagnostics);

    /// <summary>The text a live control shows, whichever markup populated it.</summary>
    private static string Shown(LiveControl control) =>
        control.Content switch
        {
            TextBlock text => text.Text ?? string.Empty,
            Panel panel => panel.Children.OfType<TextBlock>().Single().Text ?? string.Empty,
            _ => string.Empty,
        };

    [AvaloniaFact]
    public async Task APlacedControlIsBuiltAgainFromItsNewMarkup()
    {
        await using XamlLoadSession session = await LoadAsync(Form("<tc:LiveControl x:Name=\"Placed\" />"));

        var panel = session.GetRoot<StackPanel>();
        TextBlock kept = panel.Children.OfType<TextBlock>().Single();
        LiveControl before = panel.Children.OfType<LiveControl>().Single();

        Assert.Equal("Compiled", Shown(before));

        using var population = new XamlLivePopulation(Environment());

        Assert.True((await population.SetDocumentAsync(
            typeof(LiveControl), LiveControlDocument("Edited"), TestContext.Current.CancellationToken)).Success);

        XamlUpdateResult result = await session.ApplyRebuildAsync(
            [Named(session, "Placed")], TestContext.Current.CancellationToken);

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.ReloadSubtree, result.Strategy);

        LiveControl after = panel.Children.OfType<LiveControl>().Single();

        // A new instance, populated from the markup the class has now; nothing else was touched.
        Assert.NotSame(before, after);
        Assert.Equal("Edited", Shown(after));
        Assert.Same(kept, panel.Children.OfType<TextBlock>().Single());
        Assert.Same(after, session.GetObject(Named(session, "Placed")));
    }

    [AvaloniaFact]
    public async Task TheRootIsNotRebuiltInPlace()
    {
        await using XamlLoadSession session = await LoadAsync(Form("<tc:LiveControl />"));

        object root = session.RootObject;

        XamlUpdateResult result = await session.ApplyRebuildAsync(
            [session.Document.Root!], TestContext.Current.CancellationToken);

        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlUpdateStrategy.RecreateSession, result.Strategy);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UpdateRequiresNewSession);
        Assert.Same(root, session.RootObject);
    }

    [AvaloniaFact]
    public async Task AnElementOfAReplacedDocumentIsRefused()
    {
        await using XamlLoadSession session = await LoadAsync(Form("<tc:LiveControl x:Name=\"Placed\" />"));

        XamlElement found = Named(session, "Placed");

        Assert.True((await session.ApplyDocumentUpdateAsync(
            Form("<tc:LiveControl x:Name=\"Placed\" Width=\"40\" />"), TestContext.Current.CancellationToken)).Applied);

        LiveControl placed = session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single();

        XamlUpdateResult result = await session.ApplyRebuildAsync([found], TestContext.Current.CancellationToken);

        // Refused before anything was worked out for it: no strategy, no changes.
        Assert.Equal(XamlUpdateOutcome.RejectedCleanly, result.Outcome);
        Assert.Equal(XamlUpdateStrategy.None, result.Strategy);
        Assert.Empty(result.Changes);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == XamlLoaderDiagnosticCodes.UpdateNotApplied);
        Assert.Same(placed, session.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single());
    }

    [AvaloniaFact]
    public async Task NothingAskedIsNothingDone()
    {
        await using XamlLoadSession session = await LoadAsync(Form("<tc:LiveControl />"));

        XamlUpdateResult result = await session.ApplyRebuildAsync([], TestContext.Current.CancellationToken);

        Assert.True(result.Applied, Describe(result));
        Assert.Equal(XamlUpdateStrategy.None, result.Strategy);
        Assert.Empty(result.Changes);
    }
}
