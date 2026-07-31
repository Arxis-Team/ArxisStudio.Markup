using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup;

/// <summary>
/// An immutable snapshot of document text.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots are the foundation of this library's round-trip guarantee. A snapshot never
/// changes, so spans taken against it stay valid, and any number of threads may read it
/// concurrently without synchronisation. Editing produces a new snapshot rather than mutating
/// an existing one.
/// </para>
/// <para>
/// Text is stored exactly as it was read. Line breaks, trailing whitespace and the byte-order
/// mark are never normalised, because normalising any of them would rewrite source the caller
/// did not ask to change.
/// </para>
/// </remarks>
public abstract class SourceText
{
    private TextLineCollection? _lines;

    /// <summary>Initialises a new snapshot.</summary>
    /// <param name="encoding">The encoding the text was read with, or will be written with.</param>
    /// <param name="hasByteOrderMark">Whether the original bytes carried a byte-order mark.</param>
    protected SourceText(Encoding encoding, bool hasByteOrderMark)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        Encoding = encoding;
        HasByteOrderMark = hasByteOrderMark;
    }

    /// <summary>Gets the number of characters in the snapshot.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the character at the given zero-based offset.</summary>
    /// <param name="index">The zero-based offset.</param>
    /// <returns>The character at that offset.</returns>
    public abstract char this[int index] { get; }

    /// <summary>
    /// Gets the encoding the text was read with. Retained so that writing the document back
    /// can reproduce the original bytes.
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// Gets a value indicating whether the original bytes began with a byte-order mark.
    /// Retained for the same reason as <see cref="Encoding"/>: a byte-for-byte round trip has
    /// to reproduce its presence or absence.
    /// </summary>
    public bool HasByteOrderMark { get; }

    /// <summary>Gets a value indicating whether the snapshot contains no characters.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>
    /// Gets the lines of the snapshot. Computed on first access and cached for the lifetime of
    /// the snapshot, which is safe because the snapshot is immutable.
    /// </summary>
    public TextLineCollection Lines
    {
        get
        {
            TextLineCollection? lines = Volatile.Read(ref _lines);

            if (lines is null)
            {
                lines = new TextLineCollection(this, LineBreaks.ComputeLineStarts(ToCharSpan()));

                // A racing thread may have produced an equivalent collection; either will do.
                Interlocked.CompareExchange(ref _lines, lines, null);
                lines = _lines!;
            }

            return lines;
        }
    }

    /// <summary>Gets the text covered by a span.</summary>
    /// <param name="span">The range to read.</param>
    /// <returns>The text in that range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The span extends beyond the snapshot.</exception>
    public abstract string GetText(TextSpan span);

    /// <summary>
    /// Produces a new snapshot with the given changes applied.
    /// </summary>
    /// <param name="changes">
    /// The changes to apply. They must be ordered by <see cref="TextSpan.Start"/> and must not
    /// overlap; the caller is responsible for describing a coherent edit.
    /// </param>
    /// <returns>
    /// A new snapshot, or this one when <paramref name="changes"/> is empty. The original
    /// snapshot is unaffected.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The changes are unordered or overlap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A change extends beyond the snapshot.</exception>
    public abstract SourceText WithChanges(IReadOnlyList<TextChange> changes);

    /// <summary>Produces a new snapshot with a single change applied.</summary>
    /// <param name="change">The change to apply.</param>
    /// <returns>A new snapshot. The original snapshot is unaffected.</returns>
    public SourceText WithChange(TextChange change) => WithChanges([change]);

    /// <summary>Creates a snapshot over a string.</summary>
    /// <param name="text">The text of the snapshot.</param>
    /// <param name="encoding">The encoding to associate, defaulting to UTF-8.</param>
    /// <param name="hasByteOrderMark">Whether the original bytes carried a byte-order mark.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SourceText From(string text, Encoding? encoding = null, bool hasByteOrderMark = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new StringSourceText(text, encoding ?? DefaultEncoding, hasByteOrderMark);
    }

    /// <summary>
    /// Reads a snapshot from a stream, detecting the encoding and byte-order mark so that the
    /// document can later be written back unchanged.
    /// </summary>
    /// <param name="stream">The stream to read. It is read to the end but not disposed.</param>
    /// <param name="encoding">
    /// The encoding to assume when the stream carries no byte-order mark. Defaults to UTF-8.
    /// </param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static async ValueTask<SourceText> FromAsync(
        Stream stream,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // detectEncodingFromByteOrderMarks lets an explicit mark in the bytes win over the
        // requested default, which is what round-tripping an existing file requires.
        using var reader = new StreamReader(
            stream,
            encoding ?? DefaultEncoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        // ReadToEndAsync consumes any mark, so the reader's own view of the encoding after the
        // read is the authority on what was actually there.
        bool hasByteOrderMark = reader.CurrentEncoding.GetPreamble().Length > 0
            && !ReferenceEquals(reader.CurrentEncoding, encoding ?? DefaultEncoding);

        return new StringSourceText(text, reader.CurrentEncoding, hasByteOrderMark);
    }

    /// <summary>Gets the whole snapshot as a string.</summary>
    /// <returns>The text of the snapshot.</returns>
    public override string ToString() => GetText(new TextSpan(0, Length));

    /// <summary>Gets a read-only view over the snapshot's characters.</summary>
    /// <returns>The characters of the snapshot.</returns>
    public abstract ReadOnlySpan<char> ToCharSpan();

    /// <summary>
    /// The encoding assumed when none is supplied: UTF-8 without an emitted byte-order mark.
    /// </summary>
    internal static Encoding DefaultEncoding { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>
    /// Validates that changes are ordered and non-overlapping, and lie within the snapshot.
    /// </summary>
    /// <param name="changes">The changes to validate.</param>
    /// <param name="length">The length of the snapshot the changes apply to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The changes are unordered or overlap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A change extends beyond the snapshot.</exception>
    private protected static void ValidateChanges(IReadOnlyList<TextChange> changes, int length)
    {
        ArgumentNullException.ThrowIfNull(changes);

        int previousEnd = 0;

        for (int index = 0; index < changes.Count; index++)
        {
            TextChange change = changes[index];

            if (change.Span.End > length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(changes),
                    change.Span,
                    $"Change {index} at {change.Span} extends beyond the {length} characters of the snapshot.");
            }

            if (change.Span.Start < previousEnd)
            {
                throw new ArgumentException(
                    $"Change {index} at {change.Span} is out of order or overlaps the preceding change, " +
                    "which ends at " + previousEnd.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ". Changes must be ordered by start offset and must not overlap.",
                    nameof(changes));
            }

            previousEnd = change.Span.End;
        }
    }
}
