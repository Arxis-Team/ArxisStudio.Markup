using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Character data between tags, including any entity references it contains.
/// </summary>
/// <remarks>
/// The text is exposed as written. Entity references are not expanded here: <c>&amp;amp;</c>
/// and <c>&amp;#38;</c> both mean an ampersand but are different source, and the document may
/// be written back before anyone asks what either one denotes.
/// </remarks>
public sealed class XamlText : XamlSyntaxNode
{
    internal XamlText(TextSpan span, ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics) => AttachChildren([]);
}
