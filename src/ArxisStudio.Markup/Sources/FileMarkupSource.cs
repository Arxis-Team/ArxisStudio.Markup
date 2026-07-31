using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A source backed by a file on disk, read on demand.
/// </summary>
/// <remarks>
/// The text is read afresh on every call rather than cached. Deciding when a cached read has
/// gone stale is a workspace concern, and guessing at it here would hide file changes from
/// callers that deliberately re-read.
/// </remarks>
public sealed class FileMarkupSource : MarkupSource
{
    private readonly string _path;
    private readonly Encoding? _defaultEncoding;

    /// <summary>Creates a source over a file.</summary>
    /// <param name="uri">The location this source represents, which must be a file URI.</param>
    /// <param name="defaultEncoding">
    /// The encoding to assume when the file carries no byte-order mark. Defaults to UTF-8.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is not a file URI.</exception>
    public FileMarkupSource(Uri uri, Encoding? defaultEncoding = null)
        : base(uri)
    {
        if (!uri.IsFile)
        {
            throw new ArgumentException(
                $"'{uri}' is not a file URI. Use a source that matches the scheme instead.",
                nameof(uri));
        }

        _path = uri.LocalPath;
        _defaultEncoding = defaultEncoding;
    }

    /// <summary>Gets the local filesystem path this source reads from.</summary>
    public string Path => _path;

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">The file no longer exists.</exception>
    public override async ValueTask<SourceText> GetTextAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

        return await SourceText.FromAsync(stream, _defaultEncoding, cancellationToken).ConfigureAwait(false);
    }
}
