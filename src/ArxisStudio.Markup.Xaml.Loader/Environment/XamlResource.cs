using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Content found at a resource URI.
/// </summary>
/// <remarks>
/// Resolution and reading are separate so that a graph of hundreds of resources can be
/// enumerated without opening the ones nobody asked for.
/// </remarks>
public sealed class XamlResource
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _open;

    /// <summary>Creates a resource whose content is opened on demand.</summary>
    /// <param name="uri">The URI the resource was found at.</param>
    /// <param name="open">Opens the resource's content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="open"/> is <see langword="null"/>.</exception>
    public XamlResource(Uri uri, Func<CancellationToken, ValueTask<Stream>> open)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(open);

        Uri = uri;
        _open = open;
    }

    /// <summary>Creates a resource over text already in memory.</summary>
    /// <param name="uri">The URI the resource was found at.</param>
    /// <param name="text">The resource's content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public XamlResource(Uri uri, SourceText text)
        : this(uri, _ => new ValueTask<Stream>(
            new MemoryStream((text ?? throw new ArgumentNullException(nameof(text))).Encoding.GetBytes(text.ToString()))))
    {
    }

    /// <summary>Gets the URI the resource was found at.</summary>
    public Uri Uri { get; }

    /// <summary>Opens the resource's content.</summary>
    /// <param name="cancellationToken">A token to observe while opening.</param>
    /// <returns>The content, which the caller disposes.</returns>
    public ValueTask<Stream> OpenAsync(CancellationToken cancellationToken = default) => _open(cancellationToken);

    /// <summary>Reads the resource as a text snapshot.</summary>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>The resource's text, with its encoding and byte-order mark recorded.</returns>
    public async ValueTask<SourceText> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        await using Stream stream = await OpenAsync(cancellationToken).ConfigureAwait(false);

        return await SourceText.FromAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the resource's URI.</summary>
    /// <returns>A readable description of the resource.</returns>
    public override string ToString() => XamlUri.ToDisplayString(Uri);
}
