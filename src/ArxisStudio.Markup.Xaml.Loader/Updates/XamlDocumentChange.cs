using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// One difference between the document a session loaded and the one it is being given.
/// </summary>
/// <remarks>
/// Both sides are kept. The old element is how the change reaches the object that already
/// exists — the map is keyed on the document the objects were built from — and the new one is
/// what that object has to be brought in line with.
/// </remarks>
public sealed class XamlDocumentChange
{
    internal XamlDocumentChange(
        XamlUpdateStrategy strategy,
        XamlElement? oldElement,
        XamlElement? newElement,
        string? memberName)
    {
        Strategy = strategy;
        OldElement = oldElement;
        NewElement = newElement;
        MemberName = memberName;
    }

    /// <summary>Gets the smallest strategy certainly enough to apply this change.</summary>
    public XamlUpdateStrategy Strategy { get; }

    /// <summary>
    /// Gets the element in the loaded document, which is the one the object map knows, or
    /// <see langword="null"/> when the change is not about a particular element.
    /// </summary>
    public XamlElement? OldElement { get; }

    /// <summary>Gets the corresponding element in the new document, when there is one.</summary>
    public XamlElement? NewElement { get; }

    /// <summary>
    /// Gets the member that changed, when the change is one attribute rather than a whole
    /// element.
    /// </summary>
    public string? MemberName { get; }

    /// <summary>Returns the strategy and what it applies to.</summary>
    /// <returns>A readable description of the change.</returns>
    public override string ToString() =>
        MemberName is null
            ? $"{Strategy} <{OldElement?.Name ?? NewElement?.Name}>"
            : $"{Strategy} <{OldElement?.Name ?? NewElement?.Name}>.{MemberName}";
}
