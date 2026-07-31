using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>A <c>&lt;!-- --&gt;</c> comment.</summary>
/// <remarks>
/// Comments are part of the source and survive every edit. A tool that reformats a document
/// and loses its comments has destroyed information the author put there deliberately.
/// </remarks>
public sealed class XamlComment : XamlSyntaxNode
{
    internal XamlComment(TextSpan span, TextSpan contentSpan, ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        ContentSpan = contentSpan;
        AttachChildren([]);
    }

    /// <summary>Gets the range between the delimiters, excluding them.</summary>
    public TextSpan ContentSpan { get; }

    /// <summary>Gets the comment's text, without its delimiters.</summary>
    /// <returns>The commented text.</returns>
    public string GetContent() => Document.SourceText.GetText(ContentSpan);
}
