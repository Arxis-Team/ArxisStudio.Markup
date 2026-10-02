using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Controls;
using ArxisStudio.Markup.Xaml.Loader.Sample.Kinds;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// What each file of a project is — and, from that, how a host shows it.
/// </summary>
/// <remarks>
/// <para>
/// Two packages at work and nothing of the showcase's own deciding. <see cref="XamlStyleAnalyzer"/>
/// reads the style declarations from the syntax, and <see cref="XamlDocumentClassifier"/> resolves
/// the root and, where the root is a set of styles or a dictionary, the controls those templates
/// are for. The rows, the facts and the preview only report what the two said.
/// </para>
/// <para>
/// The files are real ones beside the assembly, and an edit never goes back to them: the gallery
/// classifies what is typed, which is what an editor asks of it on every keystroke.
/// </para>
/// </remarks>
internal sealed partial class KindsView : UserControl
{
    /// <summary>The gallery, in the order it is worth reading.</summary>
    private static readonly string[] FileNames =
    [
        "App.axaml",
        "MainWindow.axaml",
        "ToolPalette.axaml",
        "CustomerCard.axaml",
        "Banner.axaml",
        "Highlight.axaml",
        "StatusBadge.axaml",
        "StatusBadge.Theme.axaml",
        "ButtonTheme.axaml",
        "Palette.axaml",
        "Gradient.axaml",
        "Broken.axaml",
    ];

    private readonly ObservableCollection<DocumentEntry> _entries = [];
    private readonly ObservableCollection<StyleRow> _declarations = [];
    private readonly Report _facts = new();
    private readonly Report _notes = new();

    private readonly XamlLoadEnvironment _built;
    private readonly XamlLoadEnvironment _unbuilt;

    private XamlLoadSession? _preview;

    /// <summary>Which request is the latest, so a slower earlier one can stand down.</summary>
    private int _generation;

    /// <summary>Set while the editor is being filled, so filling it is not taken for typing.</summary>
    private bool _filling;

    private bool _started;

    public KindsView()
    {
        InitializeComponent();
        XamlEditor.Highlight(Editor);

        (_built, _) = ShowcaseEnvironment.Create();
        _unbuilt = UnbuiltProjectTypeResolver.Around(_built, typeof(StatusBadge).Assembly);

        Files.ItemsSource = _entries;
        Declarations.ItemsSource = _declarations;
        Facts.ItemsSource = _facts.Rows;
        PreviewNotes.ItemsSource = _notes.Rows;

        Files.SelectionChanged += (_, _) =>
        {
            if (Files.SelectedItem is DocumentEntry entry)
            {
                _ = ShowAsync(entry, fill: true);
            }
        };

        Declarations.SelectionChanged += (_, _) => Point();
        Editor.TextChanged += (_, _) => Schedule();
        Built.IsCheckedChanged += (_, _) => _ = ClassifyAllAsync();
    }

    private XamlLoadEnvironment Environment => Built.IsChecked == true ? _built : _unbuilt;

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!_started)
        {
            _started = true;

            _ = OpenAsync();
        }
    }

    /// <summary>Reads the gallery from disk and classifies all of it.</summary>
    private async Task OpenAsync()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Documents", "Kinds");

        foreach (string name in FileNames)
        {
            string path = Path.Combine(folder, name);
            string text = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;

            _entries.Add(new DocumentEntry(name, new Uri($"file:///Kinds/{name}"), text));
        }

        await ClassifyAllAsync();

        Files.SelectedIndex = 0;
    }

    /// <summary>Classifies every file again, after the environment changed under all of them.</summary>
    private async Task ClassifyAllAsync()
    {
        foreach (DocumentEntry entry in _entries)
        {
            entry.Update(await XamlDocumentClassifier.ClassifyAsync(Parse(entry), Environment));
        }

        if (Files.SelectedItem is DocumentEntry selected)
        {
            await ShowAsync(selected, fill: false);
        }
    }

    /// <summary>Waits for typing to settle, then classifies what was typed.</summary>
    private void Schedule()
    {
        if (_filling || Files.SelectedItem is not DocumentEntry entry)
        {
            return;
        }

        int generation = ++_generation;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (generation == _generation)
                {
                    entry.Text = Editor.Text;

                    _ = ShowAsync(entry, fill: false);
                }
            },
            TimeSpan.FromMilliseconds(300));
    }

    /// <summary>Classifies one file and shows what it is, its style declarations and its preview.</summary>
    private async Task ShowAsync(DocumentEntry entry, bool fill)
    {
        int generation = ++_generation;

        if (fill)
        {
            _filling = true;
            Editor.Text = entry.Text;
            Editor.ScrollToHome();
            _filling = false;

            FileUri.Text = entry.Uri.ToString();
        }

        XamlDocument document = Parse(entry);
        XamlDocumentClassification what = await XamlDocumentClassifier.ClassifyAsync(document, Environment);

        if (generation != _generation)
        {
            return;
        }

        entry.Update(what);
        ShowFacts(document, what);
        ShowDeclarations(document);

        _notes.Clear();
        (Control? content, XamlLoadSession? session) = await KindPreview.BuildAsync(document, what, Environment, _notes);

        if (generation != _generation)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            return;
        }

        if (_preview is not null)
        {
            await _preview.DisposeAsync();
        }

        _preview = session;
        Preview.Content = content;
        PreviewScroller.IsVisible = content is not null;
    }

    /// <summary>Lists what the classification says and what it rests on.</summary>
    private void ShowFacts(XamlDocument document, XamlDocumentClassification what)
    {
        _facts.Clear()
            .Field("вид", what.Kind.ToString())
            .Note(Meaning(what.Kind))
            .Field("корень", what.Root is null ? "—" : $"<{what.Root.Name}>")
            .Field("тип корня", what.RootType is null ? "не разрешён" : Chain(what.RootType))
            .Field("написан", what.IsCustomRoot ? "вне пространства Avalonia — свой" : "в пространстве Avalonia")
            .Field("опирается на", what.IsResolved ? "типы" : "имена")
            .Field("x:Class", what.Root?.GetDirective(XamlDirectives.Class) ?? "—")
            .Field("превью", what.Preview is null ? "нет" : $"<{what.Preview.Name}> в Design.PreviewWith");

        foreach (XamlTemplatedType templated in what.TemplatedTypes)
        {
            _facts.Field(
                "шаблон задан",
                $"{templated.Type?.Name ?? templated.Reference + " (не разрешён)"} ← {Describe(templated.Declaration)}");
        }

        _facts.Caption("ДИАГНОСТИКА").Diagnostics(what.Diagnostics, document.SourceText);
    }

    /// <summary>Lists the style declarations the syntax package finds, nested ones under their parents.</summary>
    private void ShowDeclarations(XamlDocument document)
    {
        _declarations.Clear();

        foreach (XamlStyleDeclaration declaration in XamlStyleAnalyzer.Discover(document))
        {
            _declarations.Add(new StyleRow(declaration));
        }

        NoDeclarations.IsVisible = _declarations.Count == 0;
    }

    /// <summary>Selects, in the text, what the chosen declaration targets.</summary>
    private void Point()
    {
        if (Declarations.SelectedItem is not StyleRow row || row.Span.End > Editor.Document.TextLength)
        {
            return;
        }

        Editor.Select(row.Span.Start, row.Span.Length);

        int line = Editor.Document.GetLineByOffset(row.Span.Start).LineNumber;
        Editor.ScrollTo(line, 0);
    }

    private static XamlDocument Parse(DocumentEntry entry) =>
        XamlDocument.Parse(entry.Text, new XamlParseOptions { DocumentUri = entry.Uri });

    /// <summary>Names a type and what it derives from, for as many steps as fit on a line.</summary>
    private static string Chain(Type type)
    {
        var names = new List<string>();

        for (Type? step = type; step is not null && step != typeof(object) && names.Count < 6; step = step.BaseType)
        {
            names.Add(step.Name);
        }

        return string.Join(" → ", names);
    }

    private static string Describe(XamlStyleDeclaration declaration) =>
        declaration.Kind == XamlStyleKind.Style
            ? $"Style '{declaration.Selector}'"
            : declaration.Element.GetAttribute("BasedOn") is { } basedOn
                ? $"ControlTheme через BasedOn {basedOn.GetValueText()}"
                : "ControlTheme";

    private static string Meaning(XamlDocumentKind kind) => kind switch
    {
        XamlDocumentKind.Application => "Приложение: визуального корня нет, его стили и ресурсы достаются окнам.",
        XamlDocumentKind.Window => "Окно или его наследник.",
        XamlDocumentKind.UserControl => "UserControl или его наследник.",
        XamlDocumentKind.Control => "Любой другой контрол — Avalonia или свой.",
        XamlDocumentKind.TemplatedControl =>
            "Внешний вид шаблонного контрола: стили или словарь, задающие Template контролу вне пространства Avalonia.",
        XamlDocumentKind.Styles => "Набор стилей.",
        XamlDocumentKind.ResourceDictionary => "Словарь ресурсов.",
        XamlDocumentKind.Other => "Не контрол, не стили и не словарь.",
        _ => "Тип корня не разрешился, а по имени корень не угадывается.",
    };
}
