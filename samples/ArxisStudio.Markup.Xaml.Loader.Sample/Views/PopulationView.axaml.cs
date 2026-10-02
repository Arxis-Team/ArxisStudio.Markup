using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Controls;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// A compiled control placed in other documents, following its own document as it is edited.
/// </summary>
/// <remarks>
/// <para>
/// The control is the showcase's own and was compiled with it, so without help every instance
/// shows the markup it was built from. <see cref="XamlLivePopulation"/> is the help: a document
/// registered for the type populates every instance constructed from then on, whoever constructs
/// it — a session loading another document, or plain code.
/// </para>
/// <para>
/// Instances already on screen are not touched; population happens at construction. So after
/// every registration the previews here are built again, which is the host's decision to make —
/// it knows what it is showing and the service does not.
/// </para>
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The service lives while the view is on screen: created in OnLoaded, disposed in OnUnloaded. A control has no Dispose for anyone to call.")]
internal sealed partial class PopulationView : UserControl
{
    /// <summary>
    /// The file the control was compiled from, copied beside the assembly by the project: Avalonia
    /// keeps no copy of markup it has compiled.
    /// </summary>
    private static string CompiledMarkupPath =>
        Path.Combine(AppContext.BaseDirectory, "Controls", "CustomerChip.axaml");

    private readonly Report _report = new();
    private readonly XamlLoadEnvironment _environment;

    /// <summary>
    /// The registrations, while the view is on screen. A registration is for the type and so for
    /// the whole process: leaving the section disposes it, which puts the compiled markup back,
    /// so that an edit made here populates nothing anywhere else.
    /// </summary>
    private XamlLivePopulation? _population;

    private readonly List<XamlLivePopulationFailedEventArgs> _failures = [];

    private XamlLoadSession? _host;
    private int _generation;
    private bool _filling;
    private bool _started;

    public PopulationView()
    {
        InitializeComponent();
        XamlEditor.Highlight(Editor);

        (_environment, _) = ShowcaseEnvironment.Create();

        ReportList.ItemsSource = _report.Rows;
        Editor.TextChanged += (_, _) => Schedule();
        Live.IsCheckedChanged += (_, _) => _ = RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _population = new XamlLivePopulation(_environment);

        // Raised while an instance is being constructed, when the registered document did not
        // populate it and the compiled markup did instead. The report says so after the rebuild.
        _population.PopulationFailed += (_, failure) => _failures.Add(failure);

        if (!_started)
        {
            _started = true;

            _ = OpenAsync();
        }
        else
        {
            _ = RefreshAsync();
        }
    }

    /// <inheritdoc />
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _population?.Dispose();
        _population = null;
    }

    private async Task OpenAsync()
    {
        _filling = true;
        Editor.Text = await ReadCompiledMarkupAsync();
        _filling = false;

        await RefreshAsync();
    }

    private static async Task<string> ReadCompiledMarkupAsync()
    {
        try
        {
            return await File.ReadAllTextAsync(CompiledMarkupPath);
        }
        catch (IOException error)
        {
            return $"<!-- {CompiledMarkupPath} не прочитан: {error.Message} -->";
        }
    }

    /// <summary>Waits for typing to settle, then registers what was typed.</summary>
    private void Schedule()
    {
        if (_filling)
        {
            return;
        }

        int generation = ++_generation;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (generation == _generation)
                {
                    _ = RefreshAsync();
                }
            },
            TimeSpan.FromMilliseconds(350));
    }

    /// <summary>Registers the document — or takes the registration away — and rebuilds what shows it.</summary>
    private async Task RefreshAsync()
    {
        if (_population is null)
        {
            return;
        }

        int generation = ++_generation;
        bool live = Live.IsChecked == true;

        var document = XamlDocument.Parse(Editor.Text, new XamlParseOptions { DocumentUri = Fixtures.ChipUri });

        XamlLivePopulationResult? registered = null;

        if (live)
        {
            registered = await _population.SetDocumentAsync(typeof(CustomerChip), document);
        }
        else
        {
            _population.Remove(typeof(CustomerChip));
        }

        if (generation != _generation || _population is null)
        {
            return;
        }

        // Everything constructed from here on is populated by whatever is registered now.
        _failures.Clear();

        (XamlLoadSession? session, XamlLoadResult loaded) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(Fixtures.ChipHost, new XamlParseOptions { DocumentUri = Fixtures.ChipHostUri }),
            _environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        _host = session;
        HostPreview.Content = session?.RootObject as Control;
        CodePreview.Content = new CustomerChip();

        _report.Clear()
            .Field("наполнение", live ? "из зарегистрированного документа" : "из скомпилированной разметки");

        if (registered is not null)
        {
            _report.Verdict("документ зарегистрирован для CustomerChip", registered.Installed);
        }
        else
        {
            _report.Verdict("регистрация снята — экземпляры снова берут разметку из сборки", !_population.Contains(typeof(CustomerChip)));
        }

        _report.Verdict(
            "экземпляры построены из документа, без отката на сборку",
            live && registered?.Installed == true && _failures.Count == 0);

        _report.Note(
            "Три экземпляра справа — два поставлены документом, один создан кодом — построены заново: " +
            "уже стоящие на экране регистрация не трогает.");

        _report.Caption("ДИАГНОСТИКА").Diagnostics(Diagnostics(registered, loaded), document.SourceText);
    }

    /// <summary>Everything the registration, the load and any failed population had to say.</summary>
    private IEnumerable<MarkupDiagnostic> Diagnostics(XamlLivePopulationResult? registered, XamlLoadResult loaded)
    {
        foreach (MarkupDiagnostic diagnostic in registered?.Diagnostics ?? [])
        {
            yield return diagnostic;
        }

        foreach (MarkupDiagnostic diagnostic in loaded.Diagnostics)
        {
            yield return diagnostic;
        }

        foreach (XamlLivePopulationFailedEventArgs failure in _failures)
        {
            foreach (MarkupDiagnostic diagnostic in failure.Diagnostics)
            {
                yield return diagnostic;
            }
        }
    }
}
