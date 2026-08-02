using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader.Sample.Inspector;
using ArxisStudio.Markup.Xaml.Loader.Sample.Reporting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// A property inspector built on the published API, over a real file on disk.
/// </summary>
/// <remarks>
/// <para>
/// The packages contain no inspector and gain none from this: members are classified through
/// <see cref="XamlLoadSession.GetMember"/>, the edit is a <c>SetAttribute</c> on the document, and
/// the objects are brought in line by <see cref="XamlLoadSession.ApplyDocumentUpdateAsync"/>. See
/// <c>docs/adr/0006-inspector-in-the-sample.md</c>.
/// </para>
/// <para>
/// Nothing is set on an object directly. Writing the attribute and letting the update follow is
/// what makes "applied to the preview" and "saved to the .axaml" the same act — and what keeps a
/// binding a binding, because there is no path here that could write an effective value back.
/// </para>
/// </remarks>
internal sealed partial class InspectorView : UserControl
{
    /// <summary>Properties worth offering on anything that has them.</summary>
    private static readonly string[] Common =
    [
        "Width", "Height", "Margin", "Padding", "Background", "Foreground", "BorderBrush",
        "BorderThickness", "CornerRadius", "Opacity", "IsVisible", "IsEnabled", "FontSize",
        "FontWeight", "Text", "Content", "Spacing", "Orientation", "HorizontalAlignment",
        "VerticalAlignment",
    ];

    private const string Stated = "задано в документе";
    private const string Inherited = "унаследовано: стиль, тема или значение по умолчанию";

    private readonly ObservableCollection<ObjectNode> _nodes = [];
    private readonly ObservableCollection<PropertyRow> _properties = [];
    private readonly Report _report = new();

    private XamlWorkspace? _workspace;
    private MarkupDocumentId _documentId;
    private XamlLoadSession? _session;
    private object? _selected;
    private bool _filling;
    private bool _started;

    public InspectorView()
    {
        InitializeComponent();

        Tree.ItemsSource = _nodes;
        Properties.ItemsSource = _properties;
        ReportList.ItemsSource = _report.Rows;
        FilePath.Text = Path.GetFileName(DocumentPath);
        ToolTip.SetTip(FilePath, DocumentPath);
        Controls.XamlEditor.Highlight(Markup);

        Tree.SelectionChanged += (_, _) =>
        {
            if (!_filling && Tree.SelectedItem is ObjectNode node)
            {
                _selected = node.Target;

                ShowProperties();
            }
        };

        _report.Note("правьте свойство справа");
    }

    /// <summary>Gets the file the showcase reads and writes.</summary>
    /// <remarks>
    /// The copy beside the assembly, so running the showcase edits what it loaded rather than the
    /// checked-in original. It is a real file either way, which is the point of it not being a
    /// string in this project.
    /// </remarks>
    private static string DocumentPath =>
        Path.Combine(AppContext.BaseDirectory, "Documents", "CustomerView.axaml");

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
        // Through a workspace, so that every edit below lands in a history the packages keep
        // rather than one this sample would otherwise have had to invent.
        var workspace = new XamlWorkspace(new MarkupWorkspace(new FileMarkupSourceProvider()));
        XamlDocument document;

        try
        {
            document = await workspace.OpenAsync(new Uri(DocumentPath), CancellationToken.None);
        }
        catch (IOException error)
        {
            _report.Clear().Note($"файл не прочитан: {error.Message}");

            return;
        }

        (XamlLoadEnvironment environment, _) = ShowcaseEnvironment.Create();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            document,
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Runtime });

        if (session is null)
        {
            _report.Clear().Caption("ДИАГНОСТИКА").Diagnostics(result.Diagnostics);

            return;
        }

        _workspace = workspace;
        _documentId = workspace.Workspace.Documents.Single(open => open.Uri == document.Uri).Id;
        _session = session;

        Preview.Content = SampleData.Attach(session.RootObject);
        _selected = session.RootObject;

        ShowTree();
        ShowProperties();
        ShowHistory();
        ShowMarkup();
    }

    /// <summary>Shows the document as it now reads.</summary>
    private void ShowMarkup() => Markup.Text = _session?.Document.GetText() ?? string.Empty;

    private void OnUndo(object? sender, RoutedEventArgs e) =>
        _ = Step(
            static workspace => workspace.Undo(),
            static workspace => workspace.Redo(),
            "Отменено");

    private void OnRedo(object? sender, RoutedEventArgs e) =>
        _ = Step(
            static workspace => workspace.Redo(),
            static workspace => workspace.Undo(),
            "Повторено");

    private void OnDelete(object? sender, RoutedEventArgs e) =>
        _ = EditAsync(
            static (editor, element) => editor.RemoveElement(element),
            element => $"Удалить <{element.Name}>");

    private void OnDuplicate(object? sender, RoutedEventArgs e) =>
        _ = EditAsync(Duplicate, element => $"Дублировать <{element.Name}>");

    private void OnWrap(object? sender, RoutedEventArgs e) =>
        _ = EditAsync(
            static (editor, element) => editor.WrapElement(element, "<Border Padding=\"8\"></Border>"),
            element => $"Обернуть <{element.Name}> в Border");

    /// <summary>Puts a copy of an element straight after it, among the same siblings.</summary>
    /// <remarks>
    /// The copy loses every name in it. Avalonia registers a name once per scope and refuses a
    /// second, so a copy that kept them would not load — which is a decision about what
    /// duplicating means, and therefore this tool's to make rather than the library's.
    /// </remarks>
    private static XamlDocumentEditor Duplicate(XamlDocumentEditor editor, XamlElement element)
    {
        if (element.Parent is not XamlElement parent)
        {
            return editor;
        }

        XamlElement[] siblings = [.. parent.Elements];

        return editor.InsertElement(parent, Array.IndexOf(siblings, element) + 1, Anonymous(element));
    }

    /// <summary>Renders an element's text with every name taken out of it.</summary>
    /// <remarks>
    /// Parsed inside a wrapper that declares the XAML namespace, because a fragment lifted out of
    /// its document does not carry the prefix its own <c>x:Name</c> is written with — and an
    /// attribute whose prefix is bound to nothing is not the directive it looks like.
    /// </remarks>
    private static string Anonymous(XamlElement element)
    {
        var copy = XamlDocument.Parse(
            $"<Fragment xmlns:x=\"{XamlNamespaces.Xaml}\">{element.GetText()}</Fragment>");

        XamlDocumentEditor editor = copy.Edit();

        foreach (XamlElement node in copy.DescendantElements())
        {
            if (node.GetDirectiveAttribute(XamlDirectives.Name) is { } directive)
            {
                editor.RemoveAttribute(node, directive.Name);
            }

            if (node.GetAttribute("Name") is { } attribute)
            {
                editor.RemoveAttribute(node, attribute.Name);
            }
        }

        return editor.Apply().Root!.Elements.First().GetText();
    }

    /// <summary>Records one structural edit, applies it, and lets everything follow.</summary>
    private async Task EditAsync(
        Func<XamlDocumentEditor, XamlElement, XamlDocumentEditor> record,
        Func<XamlElement, string> describe)
    {
        if (_workspace is null
            || _session is null
            || _selected is not { } target
            || _session.GetElement(target) is not { } element
            || ReferenceEquals(element, _session.Document.Root))
        {
            return;
        }

        XamlDocument document = _workspace.GetDocument(_documentId);

        // The session's element belongs to the session's document; the editor must be opened on
        // the workspace's. Both are the same text, so the same element is at the same span.
        if (Find(document, element) is not { } subject)
        {
            return;
        }

        await SyncAsync(
            _workspace.Apply(record(document.Edit(), subject), describe(element)),
            describe(element));
    }

    /// <summary>Finds the element that stands where another one stands, in another parse.</summary>
    private static XamlElement? Find(XamlDocument document, XamlElement element) =>
        document.DescendantElements().FirstOrDefault(candidate => candidate.Span == element.Span);

    /// <summary>Moves the history and brings everything else along.</summary>
    /// <remarks>
    /// The inverse is handed along with the move, because putting the history back is not always
    /// a matter of undoing: a refused undo has to be redone, and undoing again would step past
    /// the action the user asked about into the one before it.
    /// </remarks>
    private async Task Step(Func<XamlWorkspace, bool> move, Func<XamlWorkspace, bool> inverse, string what)
    {
        if (_workspace is null || !move(_workspace))
        {
            return;
        }

        await SyncAsync(_workspace.GetDocument(_documentId), what, inverse);
    }

    /// <summary>
    /// Brings the objects, the file and the panels in line with a document the workspace produced.
    /// </summary>
    /// <remarks>
    /// An update the objects refuse is undone in the workspace as well. The document and the
    /// objects disagreeing is the one state this library exists to prevent, and a history holding
    /// an edit the tree never took would be exactly that.
    /// </remarks>
    private async Task SyncAsync(
        XamlDocument document,
        string what,
        Func<XamlWorkspace, bool>? rollback = null)
    {
        XamlUpdateResult result = await _session!.ApplyDocumentUpdateAsync(document, CancellationToken.None);

        _report.Clear()
            .Field("действие", what)
            .Field("стратегия", result.Strategy.ToString())
            .Verdict("применено к работающим объектам", result.Applied);

        if (result.Applied)
        {
            await SaveAsync();
        }
        else
        {
            (rollback ?? (static workspace => workspace.Undo()))(_workspace!);
        }

        _report.Caption("ДИАГНОСТИКА").Diagnostics(result.Diagnostics, _session.Document.SourceText);
        ShowMarkup();

        if (_session.GetElement(_selected!) is null)
        {
            _selected = _session.RootObject;
        }

        ShowTree();
        ShowProperties();
        ShowHistory();
    }

    /// <summary>Says what undoing and redoing would do, and whether they can.</summary>
    private void ShowHistory()
    {
        UndoButton.IsEnabled = _workspace?.CanUndo == true;
        RedoButton.IsEnabled = _workspace?.CanRedo == true;

        ToolTip.SetTip(UndoButton, _workspace?.UndoDescription);
        ToolTip.SetTip(RedoButton, _workspace?.RedoDescription);
    }

    /// <summary>Lists what the document describes, in the order it describes it.</summary>
    private void ShowTree()
    {
        if (_session is null || _session.Document.Root is not { } root)
        {
            return;
        }

        _filling = true;

        _nodes.Clear();
        Add(root, 0);

        // Selection is by object, so it survives an update: the document's elements are replaced
        // wholesale by one, and the objects are what both versions have in common.
        _filling = false;
        Tree.SelectedItem = _nodes.FirstOrDefault(node => ReferenceEquals(node.Target, _selected));

        void Add(XamlElement element, int depth)
        {
            int next = depth;

            if (!element.IsPropertyElementSyntax && _session!.Objects.GetObject(element) is { } target)
            {
                _nodes.Add(new ObjectNode(target, target.GetType().Name, Detail(element), depth));

                next = depth + 1;
            }

            foreach (XamlElement child in element.Elements)
            {
                Add(child, next);
            }
        }
    }

    /// <summary>
    /// Says which one this is, when the type name does not already.
    /// </summary>
    /// <remarks>
    /// A name, or the key it is filed under. Repeating the element name next to the type name it
    /// produced fills the column with <c>SolidColorBrush &lt;SolidColorBrush&gt;</c> and pushes
    /// out the part that distinguishes one row from another.
    /// </remarks>
    private static string Detail(XamlElement element)
    {
        if (element.GetDirective("Name") is { } declared)
        {
            return declared;
        }

        if (element.GetAttribute(XamlQualifiedName.Parse("Name"))?.GetValue() is XamlLiteralValue literal)
        {
            return literal.Text;
        }

        if (element.GetDirective("Key") is { } key)
        {
            return $"#{key}";
        }

        return string.Empty;
    }

    /// <summary>Builds a row for every member of the selected object worth offering.</summary>
    private void ShowProperties()
    {
        _properties.Clear();

        if (_session is null || _selected is not { } target || _session.GetElement(target) is not { } element)
        {
            Selected.Text = "—";

            return;
        }

        Selected.Text = $"{target.GetType().Name}  <{element.Name}>";

        // The root has no siblings to be duplicated among and no slot to be wrapped into: those
        // need a new session rather than an update, which is a different thing to demonstrate.
        bool structural = !ReferenceEquals(element, _session.Document.Root);

        DeleteButton.IsEnabled = structural;
        DuplicateButton.IsEnabled = structural;
        WrapButton.IsEnabled = structural;

        // What the document already says about this element first, then the rest of what it could
        // say. Which names are worth offering is the host's business; what each one means is the
        // library's, and that is the question GetMember answers.
        var names = new List<string>();

        foreach (XamlAttribute attribute in element.Attributes)
        {
            if (attribute is XamlNamespaceDeclaration
                || attribute.IsDirective
                || attribute.IsDesignTime
                || attribute.IsMarkupCompatibility)
            {
                continue;
            }

            names.Add(attribute.Name.LocalName);
        }

        names.AddRange(Common.Where(name => !names.Contains(name, StringComparer.Ordinal)));

        foreach (string name in names)
        {
            if (Row(target, element, name) is { } row)
            {
                _properties.Add(row);
            }
        }

        if (_properties.Count == 0)
        {
            _report.Clear().Note("у этого объекта нет свойств, которые можно задать атрибутом");
        }
    }

    private PropertyRow? Row(object target, XamlElement element, string name)
    {
        XamlMemberDescriptor member = _session!.GetMember(target, name);

        // An unwritable member is not something an inspector may offer, and an event is not a
        // value at all.
        if (!member.IsResolved || member.IsReadOnly || !member.CanWrite || member.Event is not null)
        {
            return null;
        }

        XamlAttribute? attribute = element.GetAttribute(XamlQualifiedName.Parse(name));

        if (attribute?.GetValue() is XamlMarkupExtensionValue)
        {
            // Shown as what it is. Offering a text box over a binding is the most natural way in
            // the world to write back what it currently evaluates to.
            return new ExpressionPropertyRow(name, "выражение в документе", attribute.GetValueText());
        }

        bool declared = attribute is not null;
        string origin = declared ? Stated : Inherited;
        string text = declared ? attribute!.GetValueText() : Effective(target, member);

        void Commit(string value) => _ = CommitAsync(name, value);

        if (member.ValueType == typeof(bool))
        {
            return new TogglePropertyRow(
                name, origin, bool.TryParse(text, out bool value) && value, Commit);
        }

        if (member.ValueType.IsEnum)
        {
            return new ChoicePropertyRow(name, origin, Enum.GetNames(member.ValueType), text, Commit);
        }

        return new TextPropertyRow(name, origin, text, Commit);
    }

    /// <summary>Reads what the object currently holds, for a member the document does not set.</summary>
    private static string Effective(object target, XamlMemberDescriptor member)
    {
        object? value = member switch
        {
            { AvaloniaProperty: { } property } when target is AvaloniaObject styled => styled.GetValue(property),
            { ClrProperty.CanRead: true } => member.ClrProperty!.GetValue(target),
            _ => null,
        };

        // An unset size reads as NaN, and showing that invites someone to type over it with
        // something that means the same and looks worse.
        return value switch
        {
            null => string.Empty,
            double number when double.IsNaN(number) => string.Empty,
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>Commits the field being typed in, rather than waiting for it to be left.</summary>
    /// <remarks>
    /// The binding writes on losing focus, which is right for a field someone is still typing in
    /// and wrong for one they have finished: pressing Enter is finishing. The row's own setter is
    /// what commits, so this is the same path as leaving the field and cannot commit twice — the
    /// binding will write the same text afterwards and the setter will see no change.
    /// </remarks>
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: TextPropertyRow row } box)
        {
            row.Text = box.Text ?? string.Empty;
            e.Handled = true;
        }
    }

    /// <summary>Writes one property into the document, and lets the objects follow.</summary>
    private async Task CommitAsync(string name, string text)
    {
        if (_workspace is null
            || _session is null
            || _selected is not { } target
            || _session.GetElement(target) is not { } element)
        {
            return;
        }

        // Asked afresh rather than remembered from when the row was built: a property the
        // document did not state is stated the moment it is first written.
        var qualified = XamlQualifiedName.Parse(name);
        bool declared = element.GetAttribute(qualified) is not null;

        // Clearing a value the document never set asks for nothing: there is no attribute to
        // remove, and writing an empty one is a conversion error rather than a default.
        if (!declared && text.Length == 0)
        {
            return;
        }

        XamlDocument document = _workspace.GetDocument(_documentId);

        if (Find(document, element) is not { } subject)
        {
            return;
        }

        string action = $"{element.Name.LocalName}.{name}";

        XamlDocument edited = _workspace.Apply(
            document.Edit().SetAttribute(subject, qualified, text), action);

        XamlUpdateResult result = await _session.ApplyDocumentUpdateAsync(edited, CancellationToken.None);

        _report.Clear()
            .Field("действие", action)
            .Field("стратегия", result.Strategy.ToString())
            .Verdict("применено к работающим объектам", result.Applied);

        if (result.Applied)
        {
            await SaveAsync();
        }
        else
        {
            _workspace.Undo();
        }

        _report.Caption("ДИАГНОСТИКА").Diagnostics(result.Diagnostics, _session.Document.SourceText);
        ShowHistory();
        ShowMarkup();

        // Setting a property changed the one attribute that was edited and nothing else, and the
        // row that was edited already shows what was typed in it. Rebuilding the rows here would
        // take the caret out of the field the instant Enter committed it, for no change to show.
        if (result.Applied && result.Strategy == XamlUpdateStrategy.SetProperty)
        {
            foreach (PropertyRow row in _properties.Where(row => string.Equals(row.Name, name, StringComparison.Ordinal)))
            {
                row.Origin = Stated;
            }

            return;
        }

        // Anything larger did more than it was asked, or nothing at all. Either way the rows and
        // the tree are rebuilt: the objects are what selection is keyed on, and they survive.
        ShowTree();
        ShowProperties();
    }

    /// <summary>Writes the document back to the file it was read from.</summary>
    private async Task SaveAsync()
    {
        try
        {
            await File.WriteAllTextAsync(DocumentPath, _session!.Document.GetText(), CancellationToken.None)
                .ConfigureAwait(true);

            _report.Verdict("сохранено в .axaml", true);
        }
        catch (IOException error)
        {
            _report.Verdict($"не сохранено: {error.Message}", false);
        }
    }
}
