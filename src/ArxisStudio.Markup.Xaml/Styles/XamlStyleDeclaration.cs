using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A <c>&lt;Style&gt;</c> or <c>&lt;ControlTheme&gt;</c> in the document: what it applies to and
/// which properties it sets, as far as the text says.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Targets"/> answers the question a tool asks first — <em>whose</em> style is this —
/// and answers it the same way for both kinds. A control theme names its type in
/// <c>TargetType</c>. A style names it in its selector, and the type that counts is the one the
/// setters land on: the last step of the selector, so <c>StackPanel &gt; Button</c> targets
/// <c>Button</c> and <c>Button /template/ Border</c> targets the <c>Border</c> inside the
/// template.
/// </para>
/// <para>
/// A nested style that begins with <c>^</c> applies to whatever its parent applies to, and takes
/// its parent's targets — including from a control theme, whose nested styles are how its states
/// are written.
/// </para>
/// </remarks>
public sealed class XamlStyleDeclaration
{
    internal XamlStyleDeclaration(
        XamlElement element,
        XamlStyleKind kind,
        XamlStyleDeclaration? parent,
        string? selector,
        ImmutableArray<XamlTypeReference> targets,
        ImmutableArray<XamlStyleSetter> setters)
    {
        Element = element;
        Kind = kind;
        Parent = parent;
        Selector = selector;
        Targets = targets;
        Setters = setters;
    }

    /// <summary>Gets the declaring element.</summary>
    public XamlElement Element { get; }

    /// <summary>Gets which of the two declarations this is.</summary>
    public XamlStyleKind Kind { get; }

    /// <summary>
    /// Gets the declaration this one is nested in, or <see langword="null"/> when it stands on
    /// its own.
    /// </summary>
    /// <remarks>
    /// Nesting means a style written directly inside another declaration, or inside its
    /// <c>Children</c>. A declaration inside a parent's <c>Resources</c>, or inside a template
    /// the parent sets, is a resource or a part of that template — not nested, and <c>^</c> in it
    /// does not reach the parent.
    /// </remarks>
    public XamlStyleDeclaration? Parent { get; }

    /// <summary>
    /// Gets the <c>Selector</c> attribute's text exactly as written, or <see langword="null"/>
    /// for a control theme or a style written without one.
    /// </summary>
    public string? Selector { get; }

    /// <summary>
    /// Gets the types the declaration's setters apply to, in the order the text names them.
    /// </summary>
    /// <remarks>
    /// Empty when the text names no type: a selector of classes or pseudo-classes alone, a
    /// selector written as a markup extension, a missing <c>TargetType</c>, or text this reader
    /// cannot follow. Reading never throws and never reports — a selector that is wrong is
    /// Avalonia's to diagnose when it loads one.
    /// </remarks>
    public ImmutableArray<XamlTypeReference> Targets { get; }

    /// <summary>
    /// Gets the setters the declaration carries itself, in source order — written directly
    /// inside it or inside its <c>Setters</c>, and not those of a nested style.
    /// </summary>
    public ImmutableArray<XamlStyleSetter> Setters { get; }

    /// <summary>Returns the kind and what it applies to.</summary>
    /// <returns>A readable description of the declaration.</returns>
    public override string ToString() =>
        Selector is { } selector
            ? $"{Kind} '{selector}'"
            : $"{Kind} {string.Join(", ", Targets.Select(static target => target.ToString()))}";
}
