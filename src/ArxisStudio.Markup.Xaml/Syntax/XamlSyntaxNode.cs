using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A node in a XAML syntax tree.
/// </summary>
/// <remarks>
/// <para>
/// Every node knows the exact range of source it came from, and the ranges of a parent's
/// children tile that parent's range without gaps or overlaps. Writing the tree back out is
/// therefore just a walk, and any hole in the tree shows up immediately as text that failed to
/// round-trip rather than as a subtle loss noticed much later.
/// </para>
/// <para>
/// Nodes are immutable. Editing produces a new tree in a later milestone; nothing mutates a
/// node in place.
/// </para>
/// </remarks>
public abstract class XamlSyntaxNode
{
    private ImmutableArray<XamlSyntaxNode> _children;

    /// <summary>Initialises a node.</summary>
    /// <param name="span">The range of source the node covers.</param>
    /// <param name="diagnostics">Diagnostics raised for this node specifically.</param>
    private protected XamlSyntaxNode(TextSpan span, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Span = span;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
    }

    /// <summary>Gets the range of source this node covers, including everything it owns.</summary>
    public TextSpan Span { get; }

    /// <summary>Gets the diagnostics raised for this node specifically, excluding its children's.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

    /// <summary>Gets the enclosing node, or <see langword="null"/> for the document.</summary>
    public XamlSyntaxNode? Parent { get; private set; }

    /// <summary>Gets the document this node belongs to.</summary>
    public XamlDocument Document => this as XamlDocument ?? Parent?.Document
        ?? throw new InvalidOperationException("The node is not attached to a document.");

    /// <summary>Gets this node's children, in source order.</summary>
    public ImmutableArray<XamlSyntaxNode> Children => _children;

    /// <summary>
    /// Gets a value indicating whether the node was produced by an edit rather than read from
    /// source.
    /// </summary>
    /// <remarks>
    /// Always <see langword="false"/> for a parsed tree. Editing introduces synthesized nodes
    /// in a later milestone, and the writer needs to tell them apart: a node from source is
    /// written by copying its original text, which is what leaves untouched regions untouched.
    /// </remarks>
    public virtual bool IsSynthesized => false;

    /// <summary>
    /// Gets this node's text as it would be written out.
    /// </summary>
    /// <remarks>
    /// For a parsed, unedited node this is identical to <see cref="GetSourceText"/>. The two
    /// diverge once editing introduces synthesized nodes: this one renders them, while
    /// <see cref="GetSourceText"/> keeps reporting what the file actually said.
    /// </remarks>
    /// <returns>The node's text.</returns>
    public string GetText() => XamlWriter.WritePreserve(this);

    /// <summary>Gets the original source text this node was parsed from.</summary>
    /// <returns>The text the node covers in the snapshot, exactly as written.</returns>
    public string GetSourceText() => Document.SourceText.GetText(Span);

    /// <summary>Enumerates this node's descendants, depth-first in source order.</summary>
    /// <returns>Every node beneath this one.</returns>
    public IEnumerable<XamlSyntaxNode> Descendants()
    {
        foreach (XamlSyntaxNode child in _children)
        {
            yield return child;

            foreach (XamlSyntaxNode descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Enumerates the elements beneath this node, depth-first in source order.</summary>
    /// <returns>Every element beneath this one.</returns>
    public IEnumerable<XamlElement> DescendantElements()
    {
        foreach (XamlSyntaxNode node in Descendants())
        {
            if (node is XamlElement element)
            {
                yield return element;
            }
        }
    }

    /// <summary>Enumerates this node and its ancestors, innermost first.</summary>
    /// <returns>This node, then each enclosing node up to the document.</returns>
    public IEnumerable<XamlSyntaxNode> AncestorsAndSelf()
    {
        for (XamlSyntaxNode? node = this; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    /// <summary>Finds the innermost node covering an offset.</summary>
    /// <param name="position">The zero-based offset.</param>
    /// <returns>The innermost node containing the offset, or <see langword="null"/> when it lies outside this node.</returns>
    public XamlSyntaxNode? FindNode(int position)
    {
        if (!Span.Contains(position) && !(Span.IsEmpty && Span.Start == position))
        {
            return null;
        }

        foreach (XamlSyntaxNode child in _children)
        {
            XamlSyntaxNode? found = child.FindNode(position);

            if (found is not null)
            {
                return found;
            }
        }

        return this;
    }

    /// <summary>Gets every diagnostic on this node and everything beneath it, in source order.</summary>
    /// <returns>The diagnostics of this subtree.</returns>
    public IEnumerable<MarkupDiagnostic> GetAllDiagnostics()
    {
        foreach (MarkupDiagnostic diagnostic in Diagnostics)
        {
            yield return diagnostic;
        }

        foreach (XamlSyntaxNode child in _children)
        {
            foreach (MarkupDiagnostic diagnostic in child.GetAllDiagnostics())
            {
                yield return diagnostic;
            }
        }
    }

    /// <summary>Returns the node's kind and span.</summary>
    /// <returns>A readable description of the node.</returns>
    public override string ToString() => $"{GetType().Name}{Span}";

    /// <summary>
    /// Attaches children and takes ownership of them.
    /// </summary>
    /// <remarks>
    /// Called once, by the constructor of the derived node. Parent links are set here rather
    /// than passed in because a child cannot know its parent before the parent exists.
    /// </remarks>
    private protected void AttachChildren(ImmutableArray<XamlSyntaxNode> children)
    {
        _children = children.IsDefault ? [] : children;

        foreach (XamlSyntaxNode child in _children)
        {
            child.Parent = this;
        }
    }
}
