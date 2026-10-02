namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// One <c>&lt;Setter&gt;</c> of a style declaration, as written.
/// </summary>
/// <remarks>
/// The property stays text. <c>Background</c>, <c>Grid.Row</c> and <c>(Grid.Row)</c> are all ways
/// to write one, and which member each names — and whether it exists — needs the target type,
/// which this package cannot resolve.
/// </remarks>
public sealed class XamlStyleSetter
{
    private const string PropertyAttribute = "Property";

    internal XamlStyleSetter(XamlElement element)
    {
        Element = element;
        Property = element.GetAttribute(PropertyAttribute) is { HasValue: true } property
            ? property.GetValueText()
            : null;
    }

    /// <summary>Gets the <c>&lt;Setter&gt;</c> element.</summary>
    public XamlElement Element { get; }

    /// <summary>
    /// Gets the <c>Property</c> attribute's text exactly as written, or <see langword="null"/>
    /// when the setter has none.
    /// </summary>
    public string? Property { get; }

    /// <summary>Returns the property the setter names.</summary>
    /// <returns>A readable description of the setter.</returns>
    public override string ToString() => $"Setter {Property}";
}
