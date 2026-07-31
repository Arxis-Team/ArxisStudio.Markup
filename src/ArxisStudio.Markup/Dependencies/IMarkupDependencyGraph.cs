using System.Collections.Generic;

namespace ArxisStudio.Markup;

/// <summary>
/// A directed graph of document-to-document dependencies.
/// </summary>
/// <remarks>
/// <para>
/// This package supplies graph behaviour and nothing else. It has no idea what creates a
/// dependency — that a <c>ResourceInclude</c> or a <c>StyleInclude</c> makes one document
/// depend on another is knowledge that belongs to <c>ArxisStudio.Markup.Xaml</c>, which
/// discovers the edges and feeds them here.
/// </para>
/// <para>
/// The reverse direction is what the graph exists for: when one document changes, its
/// dependents are the documents whose analysis is now stale, and invalidating exactly those
/// is what makes editing one file in a resource graph of hundreds cheap.
/// </para>
/// </remarks>
public interface IMarkupDependencyGraph
{
    /// <summary>Replaces the set of documents a document depends on.</summary>
    /// <param name="document">The dependent document.</param>
    /// <param name="dependencies">
    /// Everything it now depends on. Passing an empty collection removes its outgoing edges.
    /// </param>
    void SetDependencies(MarkupDocumentId document, IReadOnlyCollection<MarkupDocumentId> dependencies);

    /// <summary>Gets the documents a document depends on directly.</summary>
    /// <param name="document">The dependent document.</param>
    /// <returns>Its direct dependencies, empty when it has none.</returns>
    IReadOnlySet<MarkupDocumentId> GetDependencies(MarkupDocumentId document);

    /// <summary>Gets the documents that depend on a document directly.</summary>
    /// <param name="document">The depended-upon document.</param>
    /// <returns>Its direct dependents, empty when it has none.</returns>
    IReadOnlySet<MarkupDocumentId> GetDependents(MarkupDocumentId document);
}
