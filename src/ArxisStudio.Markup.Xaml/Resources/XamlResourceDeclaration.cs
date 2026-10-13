namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A keyed entry of a resource dictionary the document writes: the key, the element that is the value, whose
/// resources it belongs to and which theme it is for.
/// </summary>
/// <remarks>
/// <para>
/// An entry is an element with an <c>x:Key</c> inside a dictionary — a <c>Resources</c> property element, a
/// <c>ResourceDictionary</c> written in one or at the root of a dictionary document, a dictionary merged into
/// another (<c>MergedDictionaries</c>), or a theme dictionary (<c>ThemeDictionaries</c>). The theme dictionary's
/// own <c>x:Key</c> — <c>Light</c>, <c>Dark</c> — names its variant and is not an entry.
/// </para>
/// <para>
/// Reading is syntactic, like the includes: a dictionary is one because of what it is called, and the value is
/// whatever its element is — no type is resolved. What an include brings in lives in the included document.
/// </para>
/// </remarks>
public sealed class XamlResourceDeclaration
{
    internal XamlResourceDeclaration(XamlElement element, XamlAttribute keyAttribute, XamlElement owner, string? themeVariant)
    {
        Element = element;
        KeyAttribute = keyAttribute;
        Owner = owner;
        ThemeVariant = themeVariant;
    }

    /// <summary>Gets the element that is the value.</summary>
    public XamlElement Element { get; }

    /// <summary>Gets the <c>x:Key</c> attribute.</summary>
    public XamlAttribute KeyAttribute { get; }

    /// <summary>
    /// Gets the key: the text of a key written as text, or the expression's text for one written as a markup
    /// extension — <c>{x:Type Button}</c>.
    /// </summary>
    public string Key => KeyAttribute.GetValue() is XamlLiteralValue literal ? literal.Text : KeyAttribute.GetValueText();

    /// <summary>
    /// Gets a value indicating whether the key is written as text — the only kind <c>StaticResource</c> and
    /// <c>DynamicResource</c> name by text.
    /// </summary>
    public bool IsTextKey => KeyAttribute.GetValue() is XamlLiteralValue;

    /// <summary>Gets where the key's value is written, for a diagnostic or a jump to the definition.</summary>
    public TextSpan KeySpan => KeyAttribute.ValueSpan ?? KeyAttribute.Span;

    /// <summary>
    /// Gets the element whose resources the entry belongs to: the control, style or application that writes the
    /// <c>Resources</c>, or the root of a dictionary document.
    /// </summary>
    public XamlElement Owner { get; }

    /// <summary>
    /// Gets the key of the theme dictionary the entry is in — <c>Light</c>, <c>Dark</c>, a variant of the
    /// application's own — or <see langword="null"/> outside theme dictionaries.
    /// </summary>
    public string? ThemeVariant { get; }

    /// <summary>Returns the key, the theme and the value's element name.</summary>
    /// <returns>A readable description of the entry.</returns>
    public override string ToString() =>
        ThemeVariant is null ? $"{Key} → {Element.Name}" : $"{Key} [{ThemeVariant}] → {Element.Name}";
}
