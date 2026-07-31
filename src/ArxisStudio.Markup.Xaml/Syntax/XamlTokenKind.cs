namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// What a <see cref="XamlToken"/> is.
/// </summary>
/// <remarks>
/// The kinds are deliberately fine-grained. Quotes, the equals sign and the whitespace between
/// attributes are all separate tokens rather than details folded into a larger one, because
/// each of them is source text that has to come back out unchanged.
/// </remarks>
public enum XamlTokenKind
{
    /// <summary>Text that no other kind claimed. Only produced by a bug in the lexer.</summary>
    None,

    /// <summary>The <c>&lt;</c> that opens a start tag.</summary>
    LessThan,

    /// <summary>The <c>&lt;/</c> that opens an end tag.</summary>
    LessThanSlash,

    /// <summary>The <c>&gt;</c> that closes a tag.</summary>
    GreaterThan,

    /// <summary>The <c>/&gt;</c> that closes a self-closing element.</summary>
    SlashGreaterThan,

    /// <summary>An XML name, or one colon-separated part of one.</summary>
    Name,

    /// <summary>The colon separating a prefix from a local name.</summary>
    Colon,

    /// <summary>The equals sign between an attribute name and its value.</summary>
    Equals,

    /// <summary>A single or double quote delimiting an attribute value.</summary>
    Quote,

    /// <summary>Literal text inside an attribute value.</summary>
    AttributeValueText,

    /// <summary>Spaces and tabs, never normalised.</summary>
    Whitespace,

    /// <summary>A line break, kept exactly as written.</summary>
    NewLine,

    /// <summary>A whole <c>&lt;!-- --&gt;</c> comment, including its delimiters.</summary>
    Comment,

    /// <summary>A whole <c>&lt;![CDATA[ ]]&gt;</c> section, including its delimiters.</summary>
    CData,

    /// <summary>A whole <c>&lt;? ?&gt;</c> processing instruction.</summary>
    ProcessingInstruction,

    /// <summary>The <c>&lt;?xml ?&gt;</c> declaration.</summary>
    XmlDeclaration,

    /// <summary>A whole <c>&lt;!DOCTYPE &gt;</c> declaration.</summary>
    DocumentType,

    /// <summary>An entity or character reference such as <c>&amp;amp;</c>, kept unexpanded.</summary>
    EntityReference,

    /// <summary>Character data between tags.</summary>
    Text,

    /// <summary>Text the lexer could not make sense of, retained so it can still be written back.</summary>
    Skipped,

    /// <summary>The zero-length token at the end of the document.</summary>
    EndOfFile,
}
