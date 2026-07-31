using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Files a document pulls in: what depends on what, and what one changed file costs.
/// </summary>
internal sealed class ResourcesTab
{
    private readonly StackPanel _report = new() { Spacing = 4 };
    private readonly PreviewHost _preview = Ui.Frame();
    private readonly ComboBox _accent;

    private readonly XamlLoadEnvironment _environment;
    private readonly InMemoryResourceResolver _resources;

    private XamlLoadSession? _session;

    internal ResourcesTab()
    {
        (_environment, _resources) = ShowcaseEnvironment.Create();

        _accent = new ComboBox
        {
            ItemsSource = new[] { "#FF3366CC", "#FFCC3366", "#FF33CC66" },
            SelectedIndex = 0,
            Width = 160,
        };

        _accent.SelectionChanged += (_, _) => _ = ChangeAccentAsync();

        Content = Ui.Page(Ui.Stack(
            Ui.Heading("Files the document only includes"),
            Ui.Body(
                "The palette and the brand file exist only in memory: the includes resolve " +
                "through the environment's own resolver, which is what lets an unsaved edit be " +
                "what the objects are built from. Change the accent and the loaded tree follows " +
                "it — the document itself does not change at all."),
            Ui.Caption("the accent the palette declares"),
            _accent,
            Ui.Caption("the objects the document describes"),
            _preview,
            _report));

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _ = LoadAsync(), Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Gets the tab's content.</summary>
    internal Control Content { get; }

    private async Task LoadAsync()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            _environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            _report.Children.Add(Ui.Diagnostics(result.Diagnostics));

            return;
        }

        _session = session;
        _preview.Preview = SampleData.Attach(session.RootObject);

        await ShowGraphAsync();
    }

    private async Task ChangeAccentAsync()
    {
        if (_session is null || _accent.SelectedItem is not string colour)
        {
            return;
        }

        _resources.Update(Fixtures.PaletteUri, Fixtures.Palette(colour));

        XamlUpdateResult result = await _session.ApplySourceUpdateAsync(Fixtures.PaletteUri, CancellationToken.None);

        await ShowGraphAsync();

        _report.Children.Insert(0, Ui.Field("strategy", result.Strategy.ToString()));
        _report.Children.Insert(1, Ui.Verdict("applied to the running objects", result.Applied));
        _report.Children.Insert(
            2,
            Ui.Verdict(
                "the document was not changed",
                _session.Document.GetText() == Fixtures.View));
    }

    private async Task ShowGraphAsync()
    {
        var provider = new InMemoryMarkupSourceProvider();

        provider.Update(Fixtures.ViewUri, Fixtures.View);
        provider.Update(Fixtures.PaletteUri, Fixtures.Palette("#FF3366CC"));
        provider.Update(Fixtures.BrandUri, Fixtures.Brand);

        var graph = new XamlResourceGraph(provider);
        XamlResourceGraphResult built = await graph.BuildAsync(Fixtures.ViewUri, CancellationToken.None);

        _report.Children.Clear();
        _report.Children.Add(Ui.Caption("the dependency graph the includes form"));
        _report.Children.Add(Ui.Field("documents reached", graph.Documents.Count.ToString()));
        _report.Children.Add(Ui.Field("the view depends on", Names(graph.GetDependencies(Fixtures.ViewUri))));
        _report.Children.Add(Ui.Field("Brand.axaml is needed by", Names(graph.GetDependents(Fixtures.BrandUri))));
        _report.Children.Add(Ui.Caption("diagnostics"));
        _report.Children.Add(Ui.Diagnostics(built.Diagnostics));
    }

    private static string Names(IEnumerable<Uri> uris) =>
        string.Join(", ", uris.Select(static uri => uri.Segments[^1]));
}
