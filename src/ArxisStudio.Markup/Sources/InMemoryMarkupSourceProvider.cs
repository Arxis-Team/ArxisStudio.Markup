using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A provider over documents held in memory.
/// </summary>
/// <remarks>
/// Placed ahead of a file provider in a <see cref="CompositeMarkupSourceProvider"/>, this is
/// how an unsaved edit shadows the file it was loaded from: the resource graph resolves
/// against what the user is currently looking at rather than what is on disk.
/// </remarks>
public sealed class InMemoryMarkupSourceProvider : IMarkupSourceProvider
{
    private readonly ConcurrentDictionary<Uri, SourceText> _sources = new();

    /// <summary>Gets the URIs this provider currently holds.</summary>
    public IReadOnlyCollection<Uri> Uris => (IReadOnlyCollection<Uri>)_sources.Keys;

    /// <summary>Adds or replaces the text held for a URI.</summary>
    /// <param name="uri">The URI to hold text for.</param>
    /// <param name="text">The text to hold.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public void Update(Uri uri, SourceText text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);

        _sources[uri] = text;
    }

    /// <summary>Adds or replaces the text held for a URI.</summary>
    /// <param name="uri">The URI to hold text for.</param>
    /// <param name="text">The text to hold.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public void Update(Uri uri, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Update(uri, SourceText.From(text));
    }

    /// <summary>Stops holding text for a URI, so that later providers can answer for it again.</summary>
    /// <param name="uri">The URI to release.</param>
    /// <returns><see langword="true"/> if the URI was held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public bool Remove(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return _sources.TryRemove(uri, out _);
    }

    /// <summary>Determines whether this provider holds text for a URI.</summary>
    /// <param name="uri">The URI to test.</param>
    /// <returns><see langword="true"/> if the URI is held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public bool Contains(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return _sources.ContainsKey(uri);
    }

    /// <summary>Stops holding text for every URI.</summary>
    public void Clear() => _sources.Clear();

    /// <inheritdoc />
    public ValueTask<MarkupSource?> TryGetSourceAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        MarkupSource? source = _sources.TryGetValue(uri, out SourceText? text)
            ? new TextMarkupSource(uri, text)
            : null;

        return new ValueTask<MarkupSource?>(source);
    }
}
