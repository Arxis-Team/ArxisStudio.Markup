using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// An attribute inside a start tag, covering its name, its equals sign, its quotes and its
/// value.
/// </summary>
/// <remarks>
/// <para>
/// Everything about how the attribute was written is recorded, down to which quote character
/// delimited the value. A tool that rewrote <c>Width='320'</c> as <c>Width="320"</c> would be
/// changing source nobody asked it to change.
/// </para>
/// <para>
/// The value is exposed as raw text. Nothing here decides whether it is a literal, a markup
/// extension, or a design-time override — that is a later milestone's work, and this package
/// never resolves it against CLR metadata at all.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This is an XML attribute, not a System.Attribute. The name is fixed by the " +
        "architectural contract, which requires the vocabulary of the format being modelled.")]
public class XamlAttribute : XamlSyntaxNode
{
    internal XamlAttribute(
        TextSpan span,
        XamlQualifiedName name,
        TextSpan nameSpan,
        TextSpan? valueSpan,
        char? quote,
        ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        Name = name;
        NameSpan = nameSpan;
        ValueSpan = valueSpan;
        Quote = quote;
        AttachChildren([]);
    }

    /// <summary>Gets the attribute's name as written, prefix included.</summary>
    public XamlQualifiedName Name { get; }

    /// <summary>Gets the range of the name.</summary>
    public TextSpan NameSpan { get; }

    /// <summary>
    /// Gets the range of the value between the quotes, or <see langword="null"/> when the
    /// attribute has no value because the source is malformed.
    /// </summary>
    public TextSpan? ValueSpan { get; }

    /// <summary>
    /// Gets the quote character the value was written with, or <see langword="null"/> when the
    /// attribute has no value.
    /// </summary>
    public char? Quote { get; }

    /// <summary>Gets a value indicating whether the attribute has a value at all.</summary>
    public bool HasValue => ValueSpan is not null;

    /// <summary>
    /// Gets the attribute's value exactly as written, with entity references left unexpanded.
    /// </summary>
    /// <returns>The raw value text, or an empty string when the attribute has no value.</returns>
    public string GetValueText() =>
        ValueSpan is { } span ? Document.SourceText.GetText(span) : string.Empty;

    /// <summary>Returns the attribute's name and span.</summary>
    /// <returns>A readable description of the attribute.</returns>
    public override string ToString() => $"{GetType().Name} {Name}{Span}";
}
