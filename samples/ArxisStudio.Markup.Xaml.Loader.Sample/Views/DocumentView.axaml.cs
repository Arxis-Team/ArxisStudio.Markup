using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Controls;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// The syntax packages on their own: round-trip, recovery, edits that disturb nothing, namespaces,
/// and one action across two documents.
/// </summary>
internal sealed partial class DocumentView : UserControl
{
    private const string RenameDescription = "Переименовать кисть Accent в Highlight";

    private static readonly string OriginalPalette = Fixtures.Palette("#FF3366CC");

    private readonly Report _workspaceReport = new();

    /// <summary>What <see cref="XamlWorkspace.DocumentChanged"/> has said, most recent last.</summary>
    private readonly List<string> _events = [];

    private XamlWorkspace? _workspace;
    private MarkupDocumentId _viewId;
    private MarkupDocumentId _paletteId;
    private bool _started;

    public DocumentView()
    {
        InitializeComponent();
        XamlEditor.Highlight(EditedText, MalformedText, ViewText, PaletteText);

        var document = XamlDocument.Parse(
            Fixtures.View, new XamlParseOptions { DocumentUri = Fixtures.ViewUri });

        Facts.ItemsSource = new Report()
            .Field("символов", Count(document.SourceText.Length))
            .Field("строк", Count(document.SourceText.Lines.Count))
            .Field("токенов", Count(document.Tokens.Length))
            .Field("элементов", Count(document.DescendantElements().Count()))
            .Verdict("GetText() возвращает исходный текст, байт в байт", document.GetText() == Fixtures.View)
            .Rows;

        ShowEdits(document);
        ShowRecovery();
        ShowNamespaces(document);
        ShowExtensions(document);

        WorkspaceReport.ItemsSource = _workspaceReport.Rows;
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!_started)
        {
            _started = true;

            _ = OpenWorkspaceAsync();
        }
    }

    /// <summary>
    /// Four kinds of edit recorded in one editor, applied as one, and what text they came to.
    /// </summary>
    private void ShowEdits(XamlDocument document)
    {
        XamlElement root = document.Root!;
        XamlElement panel = Element(document, "StackPanel");
        XamlElement save = Element(document, "Button");

        XamlDocumentEditor editor = document.Edit()
            .SetAttribute(panel, XamlQualifiedName.Parse("Spacing"), "10")
            .RemoveAttribute(root, XamlQualifiedName.Parse("d:DesignHeight"))
            .ReplaceElement(save, "<ToggleButton Name=\"Save\" Content=\"Сохранить\" Width=\"120\" />")
            .InsertElement(panel, 2, "<TextBlock Text=\"Добавлено правкой\" />");

        ImmutableArray<TextChange> changes = editor.GetTextChanges();
        XamlDocument edited = editor.Apply();
        string text = edited.GetText();

        EditedText.Text = text;

        EditFacts.ItemsSource = new Report()
            .Verdict("привязка не тронута", text.Contains("Text=\"{Binding Customer.Name}\"", StringComparison.Ordinal))
            .Verdict("d:Text не тронут", text.Contains("d:Text=\"Ada Lovelace\"", StringComparison.Ordinal))
            .Verdict(
                "странные пробелы перед Spacing не тронуты",
                text.Contains("Orientation=\"Vertical\"     Spacing=\"10\"", StringComparison.Ordinal))
            .Verdict(
                "комментарий не тронут",
                text.Contains("<!-- Ресурсы, которые использует", StringComparison.Ordinal))
            .Rows;

        var list = new Report();

        foreach (TextChange change in changes)
        {
            list.Field($"строка {Line(document, change.Span.Start)}", Describe(document, change));
        }

        ChangeList.ItemsSource = list.Rows;
    }

    private void ShowRecovery()
    {
        var malformed = XamlDocument.Parse(Fixtures.Malformed);

        MalformedText.Text = Fixtures.Malformed;

        RecoveryFacts.ItemsSource = new Report()
            .Verdict("разбор не бросил исключение", true)
            .Verdict("документ всё равно записывается точь-в-точь", malformed.GetText() == Fixtures.Malformed)
            .Caption("ДИАГНОСТИКА")
            .Diagnostics(malformed.GetDiagnostics(), malformed.SourceText)
            .Rows;
    }

    /// <summary>
    /// What the prefixes mean where they are used, which is the only thing that may be compared:
    /// nothing obliges a document to spell them x, d or mc.
    /// </summary>
    private void ShowNamespaces(XamlDocument document)
    {
        XamlElement root = document.Root!;
        XamlElement title = document.DescendantElements().First(static element => element.Identity == "Title");
        var report = new Report();

        foreach ((string prefix, string uri) in root.NamespaceContext.GetInScopeDeclarations().OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            report.Field(prefix.Length == 0 ? "xmlns" : $"xmlns:{prefix}", uri);
        }

        report
            .Field("префикс дизайна", root.NamespaceContext.LookupPrefix(XamlNamespaces.Design) ?? "—")
            .Field("mc:Ignorable", root.GetAttribute(XamlQualifiedName.Parse("mc:Ignorable"))?.GetValueText() ?? "—")
            .Field("<TextBlock Name=\"Title\"> в", title.NamespaceUri ?? "—")
            .Field("его d:Text", title.GetDesignTimeAttribute("Text") ?? "—");

        Namespaces.ItemsSource = report.Rows;
    }

    private void ShowExtensions(XamlDocument document)
    {
        var extensions = new Report();

        foreach (XamlElement element in document.DescendantElements())
        {
            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute.GetValue() is XamlMarkupExtensionValue extension)
                {
                    extensions.Field($"{element.Name}.{attribute.Name}", Describe(extension));
                }
            }
        }

        Extensions.ItemsSource = extensions.Rows;
    }

    /// <summary>Opens the view and the palette it includes in one workspace, from memory.</summary>
    private async Task OpenWorkspaceAsync()
    {
        var sources = new InMemoryMarkupSourceProvider();

        sources.Update(Fixtures.ViewUri, Fixtures.View);
        sources.Update(Fixtures.PaletteUri, OriginalPalette);

        var workspace = new XamlWorkspace(new MarkupWorkspace(sources));

        // Raised for an edit, an undo and a redo alike: to anything drawing a document, those are
        // the same event, and this is the one place the texts below are redrawn from.
        workspace.DocumentChanged += (_, e) =>
        {
            _events.Add($"{e.Kind} · {FileOf(e.DocumentId)}");

            if (e.Document is { } document)
            {
                Show(e.DocumentId, document);
            }
        };

        await workspace.OpenAsync(Fixtures.ViewUri);
        await workspace.OpenAsync(Fixtures.PaletteUri);

        // Opening is a transaction too, and would otherwise be the first thing Undo offers.
        workspace.Workspace.ClearHistory();

        _workspace = workspace;
        _viewId = Id(Fixtures.ViewUri);
        _paletteId = Id(Fixtures.PaletteUri);
        _events.Clear();

        ViewText.Text = workspace.GetDocument(_viewId).GetText();
        PaletteText.Text = workspace.GetDocument(_paletteId).GetText();

        ShowHistory("открыто");
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null)
        {
            return;
        }

        XamlDocument view = _workspace.GetDocument(_viewId);
        XamlDocument palette = _workspace.GetDocument(_paletteId);

        XamlElement title = view.DescendantElements().First(static element => element.Identity == "Title");
        XamlElement accent = palette.DescendantElements()
            .First(static element => element.GetDirective(XamlDirectives.Key) == "Accent");

        // Two editors, one per document, and one description: either both land or neither does.
        _workspace.Apply(
            RenameDescription,
            view.Edit().SetAttribute(title, XamlQualifiedName.Parse("Foreground"), "{DynamicResource Highlight}"),
            palette.Edit().SetAttribute(accent, XamlQualifiedName.Parse("x:Key"), "Highlight"));

        ShowHistory("переименовано");
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_workspace?.Undo() == true)
        {
            ShowHistory("отменено");
        }
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (_workspace?.Redo() == true)
        {
            ShowHistory("повторено");
        }
    }

    /// <summary>Says what the history holds and what the last action did to both documents.</summary>
    private void ShowHistory(string what)
    {
        if (_workspace is null)
        {
            return;
        }

        string view = _workspace.GetDocument(_viewId).GetText();
        string palette = _workspace.GetDocument(_paletteId).GetText();
        bool renamed = palette.Contains("x:Key=\"Highlight\"", StringComparison.Ordinal);

        RenameButton.IsEnabled = !renamed;
        UndoButton.IsEnabled = _workspace.CanUndo;
        RedoButton.IsEnabled = _workspace.CanRedo;
        ToolTip.SetTip(UndoButton, _workspace.UndoDescription);
        ToolTip.SetTip(RedoButton, _workspace.RedoDescription);

        _workspaceReport.Clear()
            .Field("последнее действие", what)
            .Field("отменить можно", _workspace.UndoDescription ?? "нечего")
            .Field("повторить можно", _workspace.RedoDescription ?? "нечего")
            .Field("DocumentChanged", _events.Count == 0 ? "ещё не было" : string.Join(", ", _events.TakeLast(4)));

        if (!renamed)
        {
            _workspaceReport.Verdict(
                "оба документа — ровно исходный текст, символ в символ",
                view == Fixtures.View && palette == OriginalPalette);
        }
        else
        {
            _workspaceReport.Verdict(
                "оба файла изменены одним действием истории",
                view.Contains("{DynamicResource Highlight}", StringComparison.Ordinal)
                && _workspace.UndoDescription == RenameDescription);
        }
    }

    private void Show(MarkupDocumentId id, XamlDocument document)
    {
        if (id == _viewId)
        {
            ViewText.Text = document.GetText();
        }
        else if (id == _paletteId)
        {
            PaletteText.Text = document.GetText();
        }
    }

    private MarkupDocumentId Id(Uri uri) =>
        _workspace!.Workspace.Documents.Single(open => open.Uri == uri).Id;

    private string FileOf(MarkupDocumentId id) =>
        id == _viewId ? "CustomerView.axaml" : id == _paletteId ? "Palette.axaml" : "?";

    private static XamlElement Element(XamlDocument document, string localName) =>
        document.DescendantElements().First(element => element.Name.LocalName == localName);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Line(XamlDocument document, int offset) =>
        document.SourceText.Lines.GetPosition(offset).Line + 1;

    /// <summary>Says what one text change does, shortened to a line.</summary>
    private static string Describe(XamlDocument document, TextChange change)
    {
        string removed = Shorten(document.SourceText.GetText(change.Span));
        string added = Shorten(change.NewText);

        return change.IsInsertion ? $"вставлено «{added}»"
            : change.IsDeletion ? $"удалено «{removed}»"
            : $"«{removed}» → «{added}»";
    }

    private static string Shorten(string text)
    {
        string line = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(static part => part.Trim()));

        return line.Length <= 64 ? line : string.Concat(line.AsSpan(0, 61), "…");
    }

    private static string Describe(XamlMarkupExtensionValue extension) =>
        $"{extension.TypeName} — " +
        string.Join(", ", extension.Arguments.Select(static argument => argument.ToString()));
}
