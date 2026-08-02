using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Inspector;

/// <summary>
/// One line of the tree the inspector selects from.
/// </summary>
/// <remarks>
/// <para>
/// Selection is a tree beside the preview rather than a click into it. Nothing is drawn over the
/// previewed controls and no input aimed at them is intercepted — they behave exactly as they
/// would in the application they belong to, which is the one thing a preview has to get right.
/// </para>
/// <para>
/// A line stands for an element of the document, held as a <see cref="XamlElementPath"/>. Neither
/// the element nor the object it produced would do: an edit replaces every element in the document
/// and a structural update can replace the objects, while the path is a description of where the
/// thing sits and survives both — and undo and redo with them.
/// </para>
/// <para>
/// Expanded and selected are the node's own state rather than the tree's, because every edit
/// rebuilds the nodes: what the user opened is remembered by path and put back on the new ones.
/// </para>
/// </remarks>
internal sealed class ObjectNode(
    XamlElementPath path,
    string label,
    string detail,
    ObjectNodeKind kind,
    int depth)
    : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the path to the element this line stands for.</summary>
    internal XamlElementPath Path { get; } = path;

    /// <summary>Gets the type name shown for it.</summary>
    public string Label { get; } = label;

    /// <summary>Gets what the document calls it, when it calls it anything.</summary>
    public string Detail { get; } = detail;

    /// <summary>Gets what the element is, which is what the icon says.</summary>
    internal ObjectNodeKind Kind { get; } = kind;

    /// <summary>Gets the lines under this one.</summary>
    public ObservableCollection<ObjectNode> Children { get; } = [];

    /// <summary>Gets the indentation its depth in the tree earns it.</summary>
    /// <remarks>
    /// Carried by the row's content rather than by the row itself, so that the selection behind it
    /// runs the full width of the panel the way a file tree's does.
    /// </remarks>
    public Thickness Indent { get; } = new(depth * 14, 0, 0, 0);

    /// <summary>Gets whether there is anything under this line to show.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Gets whether this line stands for an element that holds other elements.</summary>
    public bool IsContainer => Kind == ObjectNodeKind.Container;

    /// <summary>Gets whether this line stands for a control with nothing under it.</summary>
    public bool IsControl => Kind == ObjectNodeKind.Control;

    /// <summary>Gets whether this line stands for something a member declares.</summary>
    public bool IsResource => Kind == ObjectNodeKind.Resource;

    /// <summary>Gets or sets whether what is under this line is shown.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>Gets or sets whether this is the selected line.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? property = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>What a line of the tree stands for, as far as showing it goes.</summary>
internal enum ObjectNodeKind
{
    /// <summary>A control with other controls under it.</summary>
    Container,

    /// <summary>A control with nothing under it.</summary>
    Control,

    /// <summary>Something declared by a member: a brush, a style, a definition.</summary>
    Resource,
}
