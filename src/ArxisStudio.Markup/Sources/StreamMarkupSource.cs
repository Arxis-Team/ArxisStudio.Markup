using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A source backed by a stream, read once and then retained.
/// </summary>
/// <remarks>
/// Unlike a file, a stream generally cannot be read twice, so the first read is cached and
/// every later call returns the same snapshot. This is the entry point for documents that
/// arrive from an archive, a network response or an embedded resource.
/// </remarks>
public sealed class StreamMarkupSource : MarkupSource, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Encoding? _defaultEncoding;
    private readonly bool _leaveOpen;

    private Stream? _stream;
    private SourceText? _text;

    /// <summary>Creates a source over a stream.</summary>
    /// <param name="uri">The location this source represents.</param>
    /// <param name="stream">The stream to read. It is read to the end on first access.</param>
    /// <param name="defaultEncoding">
    /// The encoding to assume when the stream carries no byte-order mark. Defaults to UTF-8.
    /// </param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave the stream open after reading; otherwise the stream is
    /// disposed once its text has been captured.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="stream"/> is <see langword="null"/>.</exception>
    public StreamMarkupSource(
        Uri uri,
        Stream stream,
        Encoding? defaultEncoding = null,
        bool leaveOpen = false)
        : base(uri)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
        _defaultEncoding = defaultEncoding;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public override async ValueTask<SourceText> GetTextAsync(CancellationToken cancellationToken = default)
    {
        SourceText? text = Volatile.Read(ref _text);

        if (text is not null)
        {
            return text;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_text is not null)
            {
                return _text;
            }

            Stream stream = _stream
                ?? throw new ObjectDisposedException(nameof(StreamMarkupSource));

            _text = await SourceText.FromAsync(stream, _defaultEncoding, cancellationToken).ConfigureAwait(false);

            if (!_leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            _stream = null;

            return _text;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the gate and, unless left open, the underlying stream.</summary>
    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream?.Dispose();
        }

        _stream = null;
        _gate.Dispose();
    }
}
