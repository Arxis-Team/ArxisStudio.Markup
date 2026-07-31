using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a property is currently set to, and what the document says about it.
/// </summary>
/// <remarks>
/// Both halves are reported because they routinely disagree, and every decision about whether
/// an edit is safe depends on knowing which is which.
/// </remarks>
public sealed class XamlValueInfo
{
    internal XamlValueInfo(
        AvaloniaProperty property,
        object? effectiveValue,
        XamlValueSource source,
        XamlValue sourceValue,
        bool hasBinding)
    {
        Property = property;
        EffectiveValue = effectiveValue;
        Source = source;
        SourceValue = sourceValue;
        HasBinding = hasBinding;
    }

    /// <summary>Gets the property this describes.</summary>
    public AvaloniaProperty Property { get; }

    /// <summary>Gets the value the object is currently showing.</summary>
    public object? EffectiveValue { get; }

    /// <summary>Gets where that value came from.</summary>
    public XamlValueSource Source { get; }

    /// <summary>
    /// Gets what the document says the property is, which is
    /// <see cref="XamlUnsetValue"/> when the document does not mention it.
    /// </summary>
    public XamlValue SourceValue { get; }

    /// <summary>Gets a value indicating whether a binding is currently driving the property.</summary>
    public bool HasBinding { get; }

    /// <summary>
    /// Gets a value indicating whether writing the effective value into the document would
    /// destroy an expression the author wrote.
    /// </summary>
    /// <remarks>
    /// True whenever the document holds an expression — a binding, a resource reference —
    /// rather than a literal. Nothing in this library writes an effective value back on its
    /// own, and this is what a caller checks before doing so itself.
    /// </remarks>
    public bool WouldDestroyExpression =>
        HasBinding || SourceValue is XamlMarkupExtensionValue;

    /// <summary>Returns the source, the effective value and what the document says.</summary>
    /// <returns>A readable description of the property's state.</returns>
    public override string ToString() =>
        $"{Property.Name} = {EffectiveValue ?? "null"} ({Source}), document says {SourceValue}";
}
