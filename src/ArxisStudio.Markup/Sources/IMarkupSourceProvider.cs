using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// Resolves a document URI to a source, or reports that it does not know the URI.
/// </summary>
/// <remarks>
/// This is the only way documents enter the library. Nothing in these packages inspects
/// solutions, projects or package caches; a future project-system package supplies its
/// knowledge by implementing this interface, without any change here.
/// </remarks>
public interface IMarkupSourceProvider
{
    /// <summary>Attempts to resolve a URI to a source.</summary>
    /// <param name="uri">The URI to resolve.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>
    /// The source, or <see langword="null"/> when this provider does not know the URI. Not
    /// knowing a URI is an ordinary answer, not a failure.
    /// </returns>
    ValueTask<MarkupSource?> TryGetSourceAsync(Uri uri, CancellationToken cancellationToken);
}
