using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>A <c>&lt;![CDATA[ ]]&gt;</c> section.</summary>
/// <remarks>
/// The content is character data that the document deliberately exempted from markup rules.
/// Rewriting it as escaped text would mean the same thing and still be wrong, because it is
/// not what the author wrote.
/// </remarks>
public sealed class XamlCData : XamlSyntaxNode
{
    internal XamlCData(TextSpan span, TextSpan contentSpan, ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        ContentSpan = contentSpan;
        AttachChildren([]);
    }

    /// <summary>Gets the range between the delimiters, excluding them.</summary>
    public TextSpan ContentSpan { get; }

    /// <summary>Gets the section's content, without its delimiters.</summary>
    /// <returns>The character data.</returns>
    public string GetContent() => Document.SourceText.GetText(ContentSpan);
}
