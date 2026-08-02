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
    private const string Stated = "задано в документе";
    private const string Inherited = "унаследовано: стиль, тема или значение по умолчанию";

    private readonly ObservableCollection<ObjectNode> _nodes = [];

    /// <summary>
    /// The nodes the user has closed, by path, so that a rebuild does not reopen them.
    /// </summary>
    /// <remarks>
    /// A path is a position, so this remembers a position rather than an element: delete a node
    /// above a closed one and the node that moves into its place comes back closed. That is the
    /// trade a positional key makes, and for a fold in a tree it is the cheap side of it. What is
    /// not acceptable is the set growing for the life of the view, so paths that no longer lead
    /// anywhere are dropped whenever the tree is rebuilt.
    /// </remarks>
    private readonly HashSet<XamlElementPath> _closed = [];

    private readonly ObservableCollection<PropertyRow> _properties = [];
    private readonly Report _report = new();

    private XamlWorkspace? _workspace;
    private MarkupDocumentId _documentId;
    private XamlLoadSession? _session;
    private XamlElementPath _selected = XamlElementPath.Root;
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
                _selected = node.Path;

                ShowProperties();
            }
        };

        _report.Note("правьте свойство справа");
    }

    /// <summary>Gets the element the tree has selected, in a given version of the document.</summary>
    /// <remarks>
    /// The path is resolved against whichever document is being worked on rather than remembered
    /// as an element, because the session's document and the workspace's are two parses and an
    /// element of one is not an element of the other. That they describe the same text is what
    /// makes one path answer for both.
    /// </remarks>
    private XamlElement? ElementIn(XamlDocument document) => _selected.Resolve(document);

    /// <summary>Gets the object the selected element produced, if it produced one.</summary>
    private object? Selection =>
        _session is null ? null : ElementIn(_session.Document) is { } element
            ? _session.GetObject(element)
            : null;

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
        _selected = XamlElementPath.Root;

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
            element => $"Удалить <{element.Name}>",

            // Nothing is at that position any more, and the position now holds whatever moved up
            // into it. Selecting what contained the deleted element is the tool saying which of
            // those two it meant.
            _selected.Parent);

    private void OnDuplicate(object? sender, RoutedEventArgs e) =>
        _ = EditAsync(
            static (editor, element) => editor.DuplicateElement(element),
            element => $"Дублировать <{element.Name}>");

    private void OnWrap(object? sender, RoutedEventArgs e) =>
        _ = EditAsync(
            static (editor, element) => editor.WrapElement(element, "<Border Padding=\"8\"></Border>"),
            element => $"Обернуть <{element.Name}> в Border");

    /// <summary>Records one structural edit, applies it, and lets everything follow.</summary>
    private async Task EditAsync(
        Func<XamlDocumentEditor, XamlElement, XamlDocumentEditor> record,
        Func<XamlElement, string> describe,
        XamlElementPath? select = null)
    {
        if (_workspace is null || _session is null || _selected.Steps.IsEmpty)
        {
            return;
        }

        XamlDocument document = _workspace.GetDocument(_documentId);

        if (ElementIn(document) is not { } element)
        {
            return;
        }

        await SyncAsync(
            _workspace.Apply(record(document.Edit(), element), describe(element)),
            describe(element),
            select: select);
    }

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
        Func<XamlWorkspace, bool>? rollback = null,
        XamlElementPath? select = null)
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

        if (result.Applied && select is not null)
        {
            _selected = select;
        }

        // Whatever the path led to, the document may no longer have anything there — an undo that
        // took a subtree away is enough. The library says the path no longer resolves; where to
        // put the selection instead is the tool's answer.
        if (ElementIn(_session.Document) is null)
        {
            _selected = XamlElementPath.Root;
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

        _closed.RemoveWhere(path => path.Resolve(_session.Document) is null);
        _nodes.Clear();
        Add(root, _nodes, 0, insideMember: false);

        // Selection is a path, so it means the same element after an edit as before it — including
        // after an undo, where nothing the tree held on to the last time still exists.
        _filling = false;

        void Add(XamlElement element, ObservableCollection<ObjectNode> into, int depth, bool insideMember)
        {
            ObservableCollection<ObjectNode> below = into;
            int next = depth;

            if (_session!.GetObject(element) is { } target)
            {
                ObjectNode node = Node(element, target, depth, insideMember);

                into.Add(node);
                below = node.Children;
                next = depth + 1;
            }

            // ContentElements rather than Elements: <Border.Resources> is a member of the border
            // and not a thing standing beside its children. Walking the two separately is what puts
            // the controls above the resources, in the order someone building a screen thinks in.
            foreach (XamlElement child in element.ContentElements)
            {
                Add(child, below, next, insideMember);
            }

            // What a member holds is part of the document too — the brushes under
            // <Border.Resources> are objects with properties like any other.
            foreach (XamlElement member in element.MemberElements)
            {
                foreach (XamlElement child in member.ContentElements)
                {
                    Add(child, below, next, insideMember: true);
                }
            }
        }
    }

    /// <summary>Builds the line an element gets, with the state the last one had.</summary>
    private ObjectNode Node(XamlElement element, object target, int depth, bool insideMember)
    {
        XamlElementPath path = XamlElementPath.Of(element);

        ObjectNodeKind kind = insideMember
            ? ObjectNodeKind.Resource
            : element.ContentElements.Any() || element.MemberElements.Any()
                ? ObjectNodeKind.Container
                : ObjectNodeKind.Control;

        var node = new ObjectNode(path, target.GetType().Name, Detail(element), kind, depth)
        {
            // Open unless the user closed this one — and open regardless on the way down to what
            // is selected, so that an edit cannot leave the selection inside a closed branch.
            IsExpanded = !_closed.Contains(path) || Leads(path, _selected),
            IsSelected = path.Equals(_selected),
        };

        node.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != nameof(ObjectNode.IsExpanded) || sender is not ObjectNode line)
            {
                return;
            }

            if (line.IsExpanded)
            {
                _closed.Remove(line.Path);
            }
            else
            {
                _closed.Add(line.Path);
            }
        };

        return node;
    }

    /// <summary>Reports whether one path is on the way to another.</summary>
    private static bool Leads(XamlElementPath path, XamlElementPath target) =>
        path.Steps.Length < target.Steps.Length
        && target.Steps[..path.Steps.Length].SequenceEqual(path.Steps);

    /// <summary>
    /// Says which one this is, when the type name does not already.
    /// </summary>
    /// <remarks>
    /// The name the document gives it, which <see cref="XamlElement.Identity"/> answers, or else
    /// the key it is filed under. Repeating the element name next to the type name it produced
    /// fills the column with <c>SolidColorBrush &lt;SolidColorBrush&gt;</c> and pushes out the part
    /// that distinguishes one row from another.
    /// </remarks>
    private static string Detail(XamlElement element) =>
        element.Identity
        ?? (element.GetDirective(XamlDirectives.Key) is { } key ? $"#{key}" : string.Empty);

    /// <summary>Builds a row for every member of the selected object worth offering.</summary>
    private void ShowProperties()
    {
        _properties.Clear();

        if (_session is null
            || ElementIn(_session.Document) is not { } element
            || Selection is not { } target)
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

        // What the document already says about this element first, then everything else the type
        // has. The list comes from GetMembers rather than from a table of names kept here: which
        // members exist is the library's question, and a curated table answers it only for the
        // controls whoever wrote it thought of.
        string filter = Filter.Text ?? string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<XamlMemberDescriptor>();

        foreach (XamlAttribute attribute in element.Attributes)
        {
            if (attribute is XamlNamespaceDeclaration
                || attribute.IsDirective
                || attribute.IsDesignTime
                || attribute.IsMarkupCompatibility)
            {
                continue;
            }

            if (seen.Add(attribute.Name.LocalName))
            {
                members.Add(_session.GetMember(target, attribute.Name.LocalName));
            }
        }

        // Attached members last. They belong to whatever the element happens to sit in rather than
        // to the element, there are a great many of them, and alphabetical order would otherwise
        // open every panel on AutomationProperties.
        members.AddRange(_session.GetMembers(target)
            .Where(member => seen.Add(member.Name))
            .OrderBy(static member => member.IsAttached));

        foreach (XamlMemberDescriptor member in members)
        {
            if (filter.Length > 0
                && !member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Row(target, element, member) is { } row)
            {
                _properties.Add(row);
            }
        }

        Count.Text = $"{_properties.Count} из {members.Count}";

        // Only when the object really has nothing to offer. Saying it because a filter matched
        // nothing would be false, and it would wipe the report of the edit the user is reading.
        if (_properties.Count == 0 && filter.Length == 0)
        {
            _report.Clear().Note("у этого объекта нет свойств, которые можно задать атрибутом");
        }
    }

    /// <summary>Rebuilds the rows for a narrowed list of names.</summary>
    /// <remarks>
    /// A control has upwards of two hundred settable members and all of them are shown, so finding
    /// one by scrolling is not realistic. The box narrows the list rather than the library doing
    /// it: what is worth showing is the tool's decision, and here it is the user's.
    /// </remarks>
    private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ShowProperties();

    private PropertyRow? Row(object target, XamlElement element, XamlMemberDescriptor member)
    {
        string name = member.Name;

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
        if (_workspace is null || _session is null)
        {
            return;
        }

        XamlDocument document = _workspace.GetDocument(_documentId);

        if (ElementIn(document) is not { } element)
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

        // Asked before the document is touched at all. Writing text the member cannot hold would
        // create an undo entry, fail on the objects, and roll itself back — three events for a
        // half-typed value. The library answers the same question the update would ask.
        if (Selection is { } selected
            && XamlValue.Parse(text) is not XamlMarkupExtensionValue
            && _session.GetMember(selected, name).ConvertFromText(text) is { Succeeded: false } refused)
        {
            _report.Clear()
                .Field("не записано", $"{element.Name.LocalName}.{name}")
                .Note(refused.Error ?? string.Empty);

            return;
        }

        string action = $"{element.Name.LocalName}.{name}";

        XamlDocument edited = _workspace.Apply(
            document.Edit().SetAttribute(element, qualified, text), action);

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
        // the tree are rebuilt; selection is a path, and the path still says the same thing.
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
