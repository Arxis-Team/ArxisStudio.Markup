using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// Files a document pulls in: what depends on what, and what one changed file costs.
/// </summary>
internal sealed partial class ResourcesView : UserControl
{
    private static readonly string[] Accents = ["#FF3366CC", "#FFCC3366", "#FF33CC66"];

    private readonly Report _graph = new();
    private readonly Report _updates = new();

    private readonly XamlLoadEnvironment _environment;
    private readonly InMemoryResourceResolver _resources;

    private XamlLoadSession? _session;
    private bool _started;

    public ResourcesView()
    {
        InitializeComponent();

        (_environment, _resources) = ShowcaseEnvironment.Create();

        Accent.ItemsSource = Accents;
        Accent.SelectedIndex = 0;
        Accent.SelectionChanged += (_, _) => _ = ChangeAccentAsync();

        PaletteText.Text = Fixtures.Palette(Accents[0]);
        GraphList.ItemsSource = _graph.Rows;
        UpdateList.ItemsSource = _updates.Rows;

        _updates.Note("nothing yet — pick an accent");
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!_started)
        {
            _started = true;

            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri }),
            _environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            _updates.Clear().Caption("DIAGNOSTICS").Diagnostics(result.Diagnostics);

            return;
        }

        _session = session;
        Preview.Content = SampleData.Attach(session.RootObject);

        await ShowGraphAsync();
    }

    private async Task ChangeAccentAsync()
    {
        if (_session is null || Accent.SelectedItem is not string colour)
        {
            return;
        }

        string palette = Fixtures.Palette(colour);

        PaletteText.Text = palette;
        _resources.Update(Fixtures.PaletteUri, palette);

        XamlUpdateResult result = await _session.ApplySourceUpdateAsync(Fixtures.PaletteUri, CancellationToken.None);

        _updates.Clear()
            .Field("changed file", Fixtures.PaletteUri.Segments[^1])
            .Field("strategy", result.Strategy.ToString())
            .Verdict("applied to the running objects", result.Applied)
            .Verdict("the document was not changed", _session.Document.GetText() == Fixtures.View)
            .Caption("DIAGNOSTICS")
            .Diagnostics(result.Diagnostics);

        await ShowGraphAsync();
    }

    private async Task ShowGraphAsync()
    {
        var provider = new InMemoryMarkupSourceProvider();

        provider.Update(Fixtures.ViewUri, Fixtures.View);
        provider.Update(Fixtures.PaletteUri, PaletteText.Text ?? Fixtures.Palette(Accents[0]));
        provider.Update(Fixtures.BrandUri, Fixtures.Brand);

        var graph = new XamlResourceGraph(provider);
        XamlResourceGraphResult built = await graph.BuildAsync(Fixtures.ViewUri, CancellationToken.None);

        _graph.Clear()
            .Field("documents reached", graph.Documents.Count.ToString(CultureInfo.InvariantCulture))
            .Field("the view depends on", Names(graph.GetDependencies(Fixtures.ViewUri)))
            .Field("Brand.axaml is needed by", Names(graph.GetDependents(Fixtures.BrandUri)))
            .Caption("DIAGNOSTICS")
            .Diagnostics(built.Diagnostics);
    }

    private static string Names(IEnumerable<Uri> uris) =>
        string.Join(", ", uris.Select(static uri => uri.Segments[^1]));
}
