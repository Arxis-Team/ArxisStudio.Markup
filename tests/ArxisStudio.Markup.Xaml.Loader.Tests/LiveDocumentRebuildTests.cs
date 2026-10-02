using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Elements of a live document built again because a control they place has new markup — what a host
/// asks for when the control's own document changed and the one that places it did not.
/// </summary>
/// <remarks>
/// Rebuilding the whole document for that is a new session: a new root, the author's constructor of a
/// window run again, the map and the handlers started over — for one placed control. The session
/// builds just the elements again (<see cref="XamlLoadSession.ApplyRebuildAsync"/>), and the live
/// document is where a host asks for it, in its turn, against the text it shows.
/// </remarks>
public sealed class LiveDocumentRebuildTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "using:ArxisStudio.Markup.Xaml.Loader.TestControls";

    private static readonly Uri FormUri = new("file:///Views/Form.axaml");

    private static XamlLoadEnvironment Environment() =>
        XamlLoadEnvironment.CreateDefault([typeof(LiveControl).Assembly], new InMemoryMarkupSourceProvider());

    private static string Form(string placed) =>
        $"<StackPanel xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\" xmlns:tc=\"{ControlsNamespace}\">\n" +
        "  <TextBlock Text=\"kept\" />\n" +
        $"  {placed}\n" +
        "</StackPanel>\n";

    private static XamlDocument LiveControlDocument(string text) => XamlDocument.Parse(
        $"<UserControl xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespaces.Xaml}\"\n" +
        $"             x:Class=\"{typeof(LiveControl).FullName}\">\n" +
        $"  <TextBlock Text=\"{text}\" />\n" +
        "</UserControl>",
        new XamlParseOptions { DocumentUri = new Uri("file:///Views/LiveControl.axaml") });

    private static async Task<XamlLiveDocument> OpenAsync(string placed)
    {
        var document = XamlLiveDocument.Open(FormUri, SourceText.From(Form(placed)));

        XamlLiveEditResult attached = await document.AttachAsync(
            Environment(), new XamlLoadOptions(), TestContext.Current.CancellationToken);

        Assert.True(attached.State == XamlLiveDocumentState.Live, Describe(attached));

        return document;
    }

    /// <summary>The elements that place the live control, in the document given.</summary>
    private static IEnumerable<XamlElement> Placing(XamlDocument shown) =>
        shown.DescendantElements().Where(static element => element.Name.LocalName == nameof(LiveControl));

    /// <summary>The text a live control shows, whichever markup populated it.</summary>
    private static string Shown(LiveControl control) =>
        control.Content switch
        {
            TextBlock text => text.Text ?? string.Empty,
            Panel panel => panel.Children.OfType<TextBlock>().Single().Text ?? string.Empty,
            _ => string.Empty,
        };

    private static string Describe(XamlLiveEditResult result) =>
        $"{result.State}: " + string.Join(" | ", result.Diagnostics);

    [AvaloniaFact]
    public async Task APlacedControlIsBuiltAgainAndTheSessionStays()
    {
        await using XamlLiveDocument document = await OpenAsync("<tc:LiveControl x:Name=\"Placed\" />");

        XamlLoadSession session = document.Session!;
        var panel = session.GetRoot<StackPanel>();
        TextBlock kept = panel.Children.OfType<TextBlock>().Single();
        LiveControl before = panel.Children.OfType<LiveControl>().Single();
        var changes = new List<XamlLiveDocumentChanges>();

        document.Changed += (_, e) => changes.Add(e.Changes);

        using var population = new XamlLivePopulation(Environment());

        Assert.True((await population.SetDocumentAsync(
            typeof(LiveControl), LiveControlDocument("Edited"), TestContext.Current.CancellationToken)).Success);

        XamlLiveEditResult rebuilt = await document.RebuildAsync(Placing, TestContext.Current.CancellationToken);

        Assert.False(rebuilt.TextChanged);
        Assert.False(rebuilt.SessionReplaced);
        Assert.Equal(XamlLiveDocumentState.Live, rebuilt.State);
        Assert.True(rebuilt.Update is { Applied: true }, Describe(rebuilt));
        Assert.Same(session, document.Session);

        // A new instance of the placed control, from the markup its class has now; nothing else built.
        LiveControl after = panel.Children.OfType<LiveControl>().Single();

        Assert.NotSame(before, after);
        Assert.Equal("Edited", Shown(after));
        Assert.Same(kept, panel.Children.OfType<TextBlock>().Single());

        // The objects moved and the text did not: no step of the history, nothing more saved or unsaved.
        Assert.Equal([XamlLiveDocumentChanges.Objects], changes);
        Assert.False(document.CanUndo);
        Assert.False(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task TheRootChosenIsBuiltAsANewSession()
    {
        await using XamlLiveDocument document = await OpenAsync("<tc:LiveControl />");

        XamlLoadSession session = document.Session!;

        // The session is built around its root and cannot build it in place; the whole text is built
        // again instead, which is what rebuilding without elements does.
        XamlLiveEditResult rebuilt = await document.RebuildAsync(
            static shown => [shown.Root!], TestContext.Current.CancellationToken);

        Assert.True(rebuilt.SessionReplaced, Describe(rebuilt));
        Assert.Equal(XamlLiveDocumentState.Live, rebuilt.State);
        Assert.NotSame(session, document.Session);
        Assert.False(rebuilt.TextChanged);
    }

    [AvaloniaFact]
    public async Task NothingChosenIsNothingDone()
    {
        await using XamlLiveDocument document = await OpenAsync("<tc:LiveControl />");

        XamlLoadSession session = document.Session!;
        var raised = 0;

        document.Changed += (_, _) => raised++;

        XamlLiveEditResult rebuilt = await document.RebuildAsync(
            static _ => [], TestContext.Current.CancellationToken);

        Assert.False(rebuilt.TextChanged);
        Assert.False(rebuilt.SessionReplaced);
        Assert.Null(rebuilt.Update);
        Assert.Same(session, document.Session);
        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public async Task ADocumentNothingShowsIsNotAsked()
    {
        await using var document = XamlLiveDocument.Open(FormUri, SourceText.From(Form("<tc:LiveControl />")));

        var asked = false;

        XamlLiveEditResult rebuilt = await document.RebuildAsync(
            _ =>
            {
                asked = true;

                return [];
            },
            TestContext.Current.CancellationToken);

        Assert.False(asked);
        Assert.Equal(XamlLiveDocumentState.Detached, rebuilt.State);
        Assert.Null(document.Session);
    }

    [AvaloniaFact]
    public async Task TheElementsAreChosenFromTheTextTheTurnFinds()
    {
        await using XamlLiveDocument document = await OpenAsync("<tc:LiveControl x:Name=\"Placed\" />");

        XamlDocument? seen = null;

        // Asked one after the other, the rebuild waits for the edit's turn to end: an element chosen
        // before that would be one of a document the session no longer describes.
        ValueTask<XamlLiveEditResult> edit = document.EditAsync(
            editor => editor.SetAttribute(
                Placing(editor.Document).Single(), XamlQualifiedName.Unprefixed("Width"), "40"),
            "Size the placed control",
            TestContext.Current.CancellationToken);

        ValueTask<XamlLiveEditResult> rebuild = document.RebuildAsync(
            shown =>
            {
                seen = shown;

                return Placing(shown);
            },
            TestContext.Current.CancellationToken);

        Assert.True((await edit).TextChanged);

        XamlLiveEditResult rebuilt = await rebuild;

        Assert.True(rebuilt.Update is { Applied: true }, Describe(rebuilt));
        Assert.NotNull(seen);
        Assert.Contains("Width=\"40\"", seen.GetText(), StringComparison.Ordinal);
        Assert.Equal(40, document.Session!.GetRoot<StackPanel>().Children.OfType<LiveControl>().Single().Width);
    }
}
