using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Controls;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// Editing a document and seeing the objects follow it, without anything being compiled.
/// </summary>
/// <remarks>
/// <para>
/// The thesis of the whole library, on screen: type in the document and the tree that is already
/// running changes to match, by the smallest strategy that is certainly enough.
/// </para>
/// <para>
/// The preview is a preview. Nothing is drawn over it, nothing is selected in it, and no input
/// aimed at it is intercepted — the controls in it behave exactly as they would in the
/// application they belong to, because they are the application's controls.
/// </para>
/// </remarks>
internal sealed partial class LiveView : UserControl
{
    private readonly Report _report = new();
    private readonly XamlLoadEnvironment _environment;

    private XamlLoadSession? _session;

    /// <summary>Which keystroke a scheduled update belongs to, so an older one can stand down.</summary>
    private int _generation;

    private bool _started;

    public LiveView()
    {
        InitializeComponent();
        XamlEditor.Highlight(Editor);

        (_environment, _) = ShowcaseEnvironment.Create();

        Editor.Text = Fixtures.View;
        Editor.TextChanged += (_, _) => Schedule();
        ReportList.ItemsSource = _report.Rows;
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // The first load happens once the view is on screen, so a failure has somewhere to be
        // shown — and only once, however often the rail comes back to this tab.
        if (!_started)
        {
            _started = true;

            _ = ReloadAsync();
        }
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

        string text = Editor.Text;

        // Nothing was typed after all — the box announced a change that left the text as the
        // session already has it. Reporting a strategy for that would say the library did work it
        // did not do.
        if (_session.Document.GetText() == text)
        {
            return;
        }

        var document = XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

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
        var document = XamlDocument.Parse(Editor.Text, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document, _environment, new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            Show("объекты не построены", false, result.Diagnostics, document.SourceText, []);

            return;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = session;

        Preview.Content = SampleData.Attach(session.RootObject);

        Show("загружено", true, result.Diagnostics, document.SourceText, []);
    }

    private void Show(
        string strategy,
        bool applied,
        IReadOnlyList<MarkupDiagnostic> diagnostics,
        SourceText text,
        IReadOnlyList<XamlDocumentChange> changes)
    {
        _report.Clear()
            .Field("стратегия", strategy)
            .Verdict(applied ? "применено к работающим объектам" : "не применено", applied);

        if (_session is not null)
        {
            _report.Verdict(
                "документ по-прежнему записывается ровно так, как набран",
                _session.Document.GetText() == Editor.Text || !applied);
        }

        foreach (XamlDocumentChange change in changes.Take(6))
        {
            _report.Field("изменение", change.ToString());
        }

        _report.Caption("ДИАГНОСТИКА").Diagnostics(diagnostics, text);
    }
}
