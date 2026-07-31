using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A provider over files on disk.
/// </summary>
/// <remarks>
/// Answers only for <c>file:</c> URIs that currently exist. It performs no project or package
/// discovery of any kind — locating a file is the caller's business, and this provider only
/// reads what it is pointed at.
/// </remarks>
public sealed class FileMarkupSourceProvider : IMarkupSourceProvider
{
    private readonly Encoding? _defaultEncoding;

    /// <summary>Creates a file provider.</summary>
    /// <param name="defaultEncoding">
    /// The encoding to assume for files without a byte-order mark. Defaults to UTF-8.
    /// </param>
    public FileMarkupSourceProvider(Encoding? defaultEncoding = null) => _defaultEncoding = defaultEncoding;

    /// <inheritdoc />
    public ValueTask<MarkupSource?> TryGetSourceAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        MarkupSource? source = uri.IsFile && File.Exists(uri.LocalPath)
            ? new FileMarkupSource(uri, _defaultEncoding)
            : null;

        return new ValueTask<MarkupSource?>(source);
    }
}
