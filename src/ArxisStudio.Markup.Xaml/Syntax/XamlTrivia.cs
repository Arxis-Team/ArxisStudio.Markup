using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Source text that carries no structure: whitespace, line breaks, and text the parser could
/// not explain.
/// </summary>
/// <remarks>
/// Trivia is a first-class node rather than something hung off a neighbouring token. Indentation
/// and blank lines are exactly the kind of source this project must never disturb, and giving
/// them their own place in the tree means the writer preserves them without having to know they
/// are special.
/// </remarks>
public sealed class XamlTrivia : XamlSyntaxNode
{
    internal XamlTrivia(TextSpan span, XamlTriviaKind kind, ImmutableArray<MarkupDiagnostic> diagnostics)
        : base(span, diagnostics)
    {
        Kind = kind;
        AttachChildren([]);
    }

    /// <summary>Gets what kind of trivia this is.</summary>
    public XamlTriviaKind Kind { get; }
}
