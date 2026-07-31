using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// A source whose text is already in memory.
/// </summary>
/// <remarks>
/// This is what an unsaved editor buffer looks like to the rest of the library: a real
/// document at a real URI whose contents exist nowhere on disk.
/// </remarks>
public sealed class TextMarkupSource : MarkupSource
{
    private readonly SourceText _text;

    /// <summary>Creates a source over an existing snapshot.</summary>
    /// <param name="uri">The location this source represents.</param>
    /// <param name="text">The text of the source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public TextMarkupSource(Uri uri, SourceText text)
        : base(uri)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
    }

    /// <summary>Creates a source over a string.</summary>
    /// <param name="uri">The location this source represents.</param>
    /// <param name="text">The text of the source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public TextMarkupSource(Uri uri, string text)
        : this(uri, SourceText.From(text))
    {
    }

    /// <inheritdoc />
    public override ValueTask<SourceText> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<SourceText>(_text);
    }
}
