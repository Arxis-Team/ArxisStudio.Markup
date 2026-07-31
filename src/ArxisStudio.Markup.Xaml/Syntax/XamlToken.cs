namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// One lexical unit of a document, identified by what it is and where it is.
/// </summary>
/// <remarks>
/// A token carries no text of its own. It points into the snapshot it was lexed from, so the
/// token stream costs almost nothing next to the document and cannot drift out of agreement
/// with it.
/// </remarks>
/// <param name="Kind">What the token is.</param>
/// <param name="Span">Where it is in the source snapshot.</param>
public readonly record struct XamlToken(XamlTokenKind Kind, TextSpan Span)
{
    /// <summary>Gets a value indicating whether the token is whitespace or a line break.</summary>
    public bool IsWhitespace => Kind is XamlTokenKind.Whitespace or XamlTokenKind.NewLine;

    /// <summary>Returns the token's kind and span.</summary>
    /// <returns>A readable representation of the token.</returns>
    public override string ToString() => $"{Kind}{Span}";
}
