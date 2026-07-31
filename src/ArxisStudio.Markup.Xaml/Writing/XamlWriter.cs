using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Writes a syntax tree back out, preserving everything it did not change.
/// </summary>
/// <remarks>
/// <para>
/// The walk is deliberate rather than a shortcut. Copying the document's whole source in one
/// go would round-trip trivially and prove nothing; descending through every node and emitting
/// the gaps between children means a child span that escapes its parent, arrives out of order
/// or overlaps a sibling shows up immediately as text that failed to come back.
/// </para>
/// <para>
/// The same walk is what will preserve untouched regions once editing introduces synthesized
/// nodes: unchanged text is copied from the original snapshot, and only changed nodes are
/// rendered.
/// </para>
/// </remarks>
internal static class XamlWriter
{
    /// <summary>Writes a node and everything beneath it.</summary>
    /// <param name="node">The node to write.</param>
    /// <returns>The node's text.</returns>
    public static string WritePreserve(XamlSyntaxNode node)
    {
        var builder = new StringBuilder(node.Span.Length);

        Write(node, builder);

        return builder.ToString();
    }

    private static void Write(XamlSyntaxNode node, StringBuilder builder)
    {
        SourceText source = node.Document.SourceText;
        int position = node.Span.Start;

        foreach (XamlSyntaxNode child in node.Children)
        {
            // Whatever lies between the previous child and this one is source the node owns
            // directly — a tag's angle brackets, the whitespace between two attributes — and
            // it is copied verbatim.
            builder.Append(source.GetText(TextSpan.FromBounds(position, child.Span.Start)));

            Write(child, builder);

            position = child.Span.End;
        }

        builder.Append(source.GetText(TextSpan.FromBounds(position, node.Span.End)));
    }
}
