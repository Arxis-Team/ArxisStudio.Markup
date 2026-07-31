using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Rewrites a whole document to a chosen layout.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place in the library that deliberately changes source nobody edited, which
/// is why it is never reached without a caller naming <see cref="XamlWriteMode.Format"/>. A
/// save must never need it.
/// </para>
/// <para>
/// It reflows layout only. Names, prefixes, attribute order, comments, CDATA, entity
/// references and markup extensions are carried across as written — what changes is where the
/// line breaks and indentation fall, not what the document says.
/// </para>
/// </remarks>
internal static class XamlFormatter
{
    public static string Format(XamlDocument document, XamlFormattingOptions options)
    {
        var builder = new StringBuilder(document.SourceText.Length);

        foreach (XamlSyntaxNode node in Significant(document.Children))
        {
            WriteNode(node, builder, options, depth: 0);
            builder.Append(options.NewLine);
        }

        return builder.ToString();
    }

    private static void WriteNode(XamlSyntaxNode node, StringBuilder builder, XamlFormattingOptions options, int depth)
    {
        switch (node)
        {
            case XamlElement element:
                WriteElement(element, builder, options, depth);

                break;

            case XamlComment comment when options.PreserveComments:
                Indent(builder, options, depth);
                builder.Append(comment.GetSourceText());

                break;

            case XamlComment:
                break;

            case XamlText or XamlCData or XamlProcessingInstruction:
                Indent(builder, options, depth);
                builder.Append(node.GetSourceText().Trim());

                break;

            default:
                // Trivia is layout, and layout is what this mode is replacing.
                break;
        }
    }

    private static void WriteElement(XamlElement element, StringBuilder builder, XamlFormattingOptions options, int depth)
    {
        Indent(builder, options, depth);
        builder.Append('<').Append(element.Name);

        WriteAttributes(element, builder, options, depth);

        ImmutableArray<XamlSyntaxNode> content = [.. Significant(element.Content)];

        if (element.IsEmpty || content.IsEmpty)
        {
            builder.Append(" />");

            return;
        }

        builder.Append('>');

        // A single piece of text stays on the element's own line: breaking it out would change
        // the significant whitespace around it.
        if (content.Length == 1 && content[0] is XamlText or XamlCData)
        {
            builder.Append(content[0].GetSourceText().Trim()).Append("</").Append(element.Name).Append('>');

            return;
        }

        foreach (XamlSyntaxNode child in content)
        {
            builder.Append(options.NewLine);
            WriteNode(child, builder, options, depth + 1);
        }

        builder.Append(options.NewLine);
        Indent(builder, options, depth);
        builder.Append("</").Append(element.Name).Append('>');
    }

    private static void WriteAttributes(XamlElement element, StringBuilder builder, XamlFormattingOptions options, int depth)
    {
        foreach (XamlAttribute attribute in element.Attributes)
        {
            if (options.PutAttributesOnSeparateLines)
            {
                builder.Append(options.NewLine);
                Indent(builder, options, depth + 1);
            }
            else
            {
                builder.Append(' ');
            }

            builder.Append(attribute.Name);

            if (!attribute.HasValue)
            {
                continue;
            }

            char quote = options.AttributeQuote;
            string value = attribute.GetValueText().Replace(
                quote.ToString(),
                quote == '"' ? "&quot;" : "&apos;",
                System.StringComparison.Ordinal);

            builder.Append('=').Append(quote).Append(value).Append(quote);
        }
    }

    private static void Indent(StringBuilder builder, XamlFormattingOptions options, int depth)
    {
        for (var level = 0; level < depth; level++)
        {
            builder.Append(options.Indentation);
        }
    }

    /// <summary>Drops the whitespace nodes, since the formatter is producing its own layout.</summary>
    private static System.Collections.Generic.IEnumerable<XamlSyntaxNode> Significant(
        ImmutableArray<XamlSyntaxNode> nodes) =>
        nodes.Where(static node => node is not XamlTrivia
            && !(node is XamlText text && text.GetSourceText().Trim().Length == 0));
}
