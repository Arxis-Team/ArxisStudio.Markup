using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A resolved document location whose text can be obtained on demand.
/// </summary>
/// <remarks>
/// Resolution and reading are separate steps. A provider can answer "this URI exists here"
/// cheaply, and only the callers that actually need the characters pay for reading them —
/// which matters for resource graphs of hundreds of files where most are never opened.
/// </remarks>
public abstract class MarkupSource
{
    /// <summary>Initialises a source for a location.</summary>
    /// <param name="uri">The location this source represents.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    protected MarkupSource(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        Uri = uri;
    }

    /// <summary>Gets the location this source represents.</summary>
    public Uri Uri { get; }

    /// <summary>Reads the source's text.</summary>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A snapshot of the source's text.</returns>
    public abstract ValueTask<SourceText> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the source's location.</summary>
    /// <returns>A readable description of the source.</returns>
    public override string ToString() => Uri.ToString();
}
