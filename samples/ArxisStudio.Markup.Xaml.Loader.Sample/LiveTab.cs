using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// Editing a document and seeing the objects follow it, without anything being compiled.
/// </summary>
/// <remarks>
/// <para>
/// The thesis of the whole library, on screen: type in the document and the tree that is already
/// running changes to match, by the smallest strategy that is certainly enough. What is shown
/// next to it is which strategy that was, so the choice is visible rather than magical.
/// </para>
/// <para>
/// The preview is a preview. Nothing is drawn over it, nothing is selected in it, and no input
/// aimed at it is intercepted — the controls in it behave exactly as they would in the
/// application they belong to, because they are the application's controls.
/// </para>
/// </remarks>
internal sealed class LiveTab
{
    private readonly TextBox _editor;
    private readonly PreviewHost _preview;
    private readonly StackPanel _report;
    private readonly XamlLoadEnvironment _environment;

    private XamlLoadSession? _session;

    /// <summary>Which keystroke a scheduled update belongs to, so an older one can stand down.</summary>
    private int _generation;

    internal LiveTab()
    {
        (_environment, _) = ShowcaseEnvironment.Create();

        _editor = Ui.Code(Fixtures.View, editable: true);
        _preview = Ui.Frame();
        _report = new StackPanel { Spacing = 4 };

        _editor.TextChanged += (_, _) => Schedule();

        Content = BuildContent();

        // The first load happens once the window is up, so a failure has somewhere to be shown.
        Dispatcher.UIThread.Post(() => _ = ReloadAsync(), DispatcherPriority.Background);
    }

    /// <summary>Gets the tab's content.</summary>
    internal Control Content { get; }

    private Control BuildContent()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1*,8,1*"),
            RowDefinitions = new RowDefinitions("Auto,1*"),
            Margin = new Avalonia.Thickness(16),
        };

        StackPanel header = Ui.Stack(
            Ui.Heading("Live document"),
            Ui.Body(
                "Type in the document on the left. Nothing is compiled and no application is " +
                "restarted: the objects already running are brought in line with what you wrote, " +
                "and the strategy that took is shown below."));

        Grid.SetColumnSpan(header, 3);
        grid.Children.Add(header);

        var editorPane = new Grid { RowDefinitions = new RowDefinitions("2*,1*") };

        Grid.SetRow(_editor, 0);
        editorPane.Children.Add(_editor);

        var reportScroll = new ScrollViewer { Content = _report, Margin = new Avalonia.Thickness(0, 8, 0, 0) };

        Grid.SetRow(reportScroll, 1);
        editorPane.Children.Add(reportScroll);

        Grid.SetRow(editorPane, 1);
        Grid.SetColumn(editorPane, 0);
        grid.Children.Add(editorPane);

        var previewPane = Ui.Stack(Ui.Caption("the objects the document describes"));

        previewPane.Children.Add(_preview);

        Grid.SetRow(previewPane, 1);
        Grid.SetColumn(previewPane, 2);
        grid.Children.Add(previewPane);

        return grid;
    }

    /// <summary>Waits for typing to settle, then brings the objects in line.</summary>
    private void Schedule()
    {
        int generation = ++_generation;

        DispatcherTimer.RunOnce(
            () =>
            {
                // A later keystroke has already scheduled its own update; this one is history.
                if (generation == _generation)
                {
                    _ = UpdateAsync();
                }
            },
            TimeSpan.FromMilliseconds(350));
    }

    private async Task UpdateAsync()
    {
        if (_session is null)
        {
            await ReloadAsync();

            return;
        }

        var document = XamlDocument.Parse(
            _editor.Text ?? string.Empty, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        XamlUpdateResult result = await _session.ApplyDocumentUpdateAsync(document, CancellationToken.None);

        // Only the root changing needs a new tree; everything else was applied to the one on
        // screen, which is why the preview is not rebuilt here.
        if (!result.Applied && result.Strategy == XamlUpdateStrategy.RecreateSession)
        {
            await ReloadAsync();

            return;
        }

        Show(result.Strategy.ToString(), result.Applied, result.Diagnostics, document.SourceText, result.Changes);
    }

    /// <summary>Builds a new session, which is what a changed root leaves no choice about.</summary>
    private async Task ReloadAsync()
    {
        var document = XamlDocument.Parse(
            _editor.Text ?? string.Empty, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, _environment, new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            Show("no objects", false, result.Diagnostics, document.SourceText, []);

            return;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = session;

        _preview.Preview = SampleData.Attach(session.RootObject);

        Show("loaded", true, result.Diagnostics, document.SourceText, []);
    }

    private void Show(
        string strategy,
        bool applied,
        IReadOnlyList<MarkupDiagnostic> diagnostics,
        SourceText text,
        IReadOnlyList<XamlDocumentChange> changes)
    {
        _report.Children.Clear();
        _report.Children.Add(Ui.Field("strategy", strategy));
        _report.Children.Add(Ui.Verdict(applied ? "applied to the running objects" : "not applied", applied));

        if (_session is not null)
        {
            _report.Children.Add(Ui.Verdict(
                "the document still writes back exactly as typed",
                _session.Document.GetText() == (_editor.Text ?? string.Empty) || !applied));
        }

        foreach (XamlDocumentChange change in changes.Take(6))
        {
            _report.Children.Add(Ui.Field("change", change.ToString()));
        }

        _report.Children.Add(Ui.Caption("diagnostics"));
        _report.Children.Add(Ui.Diagnostics(diagnostics, text));
    }
}
