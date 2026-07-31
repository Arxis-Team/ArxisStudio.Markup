using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A <c>&lt;? ?&gt;</c> processing instruction, including the <c>&lt;?xml ?&gt;</c> declaration
/// and <c>&lt;!DOCTYPE&gt;</c>.
/// </summary>
/// <remarks>
/// This package does not interpret any of them. They are recognised so that they keep their
/// place in the tree and come back out unchanged — including a declaration's encoding, which
/// the caller may need when writing the document back to bytes.
/// </remarks>
public sealed class XamlProcessingInstruction : XamlSyntaxNode
{
    internal XamlProcessingInstruction(
        TextSpan span,
        XamlProcessingInstructionKind kind,
        ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        Kind = kind;
        AttachChildren([]);
    }

    /// <summary>Gets which of the prolog constructs this is.</summary>
    public XamlProcessingInstructionKind Kind { get; }

    /// <summary>Gets a value indicating whether this is the <c>&lt;?xml ?&gt;</c> declaration.</summary>
    public bool IsXmlDeclaration => Kind == XamlProcessingInstructionKind.XmlDeclaration;
}
