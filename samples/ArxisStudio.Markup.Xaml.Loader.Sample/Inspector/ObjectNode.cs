using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Inspector;

/// <summary>
/// One line of the tree the inspector selects from.
/// </summary>
/// <remarks>
/// <para>
/// Selection is a list beside the preview rather than a click into it. Nothing is drawn over the
/// previewed controls and no input aimed at them is intercepted — they behave exactly as they
/// would in the application they belong to, which is the one thing a preview has to get right.
/// </para>
/// <para>
/// A line stands for an element of the document, held as a <see cref="XamlElementPath"/>. Neither
/// the element nor the object it produced would do: an edit replaces every element in the document
/// and a structural update can replace the objects, while the path is a description of where the
/// thing sits and survives both — and undo and redo with them.
/// </para>
/// </remarks>
internal sealed class ObjectNode(XamlElementPath path, string label, string detail, int depth)
{
    /// <summary>Gets the path to the element this line stands for.</summary>
    internal XamlElementPath Path { get; } = path;

    /// <summary>Gets the type name shown for it.</summary>
    public string Label { get; } = label;

    /// <summary>Gets what the document calls it, when it calls it anything.</summary>
    public string Detail { get; } = detail;

    /// <summary>Gets the indentation its depth in the tree earns it.</summary>
    public Thickness Indent { get; } = new(depth * 14, 0, 0, 0);
}
