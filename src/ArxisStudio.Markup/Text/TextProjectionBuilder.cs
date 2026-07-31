using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace ArxisStudio.Markup;

/// <summary>
/// Assembles a <see cref="TextProjection"/> by splicing runs of other documents into one.
/// </summary>
/// <remarks>
/// <para>
/// What is spliced in is itself a projection, so a document that pulls in another which pulls in
/// a third produces one flat map covering all three. Nesting is why the replacement is given as
/// a projection and a range of it rather than as a string: a string would arrive with its own
/// origins already forgotten.
/// </para>
/// <para>
/// Replacements that overlap are rejected as they are made. An overlap means the caller has
/// asked for two different texts in the same place, which is a mistake in the caller rather than
/// something to resolve by picking one.
/// </para>
/// </remarks>
public sealed class TextProjectionBuilder
{
    private readonly SourceText _source;
    private readonly Uri? _sourceUri;
    private readonly List<Splice> _splices = [];

    /// <summary>Starts a projection of a text.</summary>
    /// <param name="source">The text to project.</param>
    /// <param name="sourceUri">The URI of the document the text belongs to, if it has one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public TextProjectionBuilder(SourceText source, Uri? sourceUri = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _sourceUri = sourceUri;
    }

    /// <summary>Gets a value indicating whether anything has been spliced in yet.</summary>
    public bool IsEmpty => _splices.Count == 0;

    /// <summary>Replaces a range of the source with part of another projection's text.</summary>
    /// <param name="span">The range of the source to replace.</param>
    /// <param name="replacement">The projection to take the replacing text from.</param>
    /// <param name="replacementSpan">The range of <see cref="TextProjection.Text"/> to take.</param>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A span extends beyond the text it indexes.</exception>
    /// <exception cref="ArgumentException"><paramref name="span"/> overlaps a replacement already made.</exception>
    public void Replace(TextSpan span, TextProjection replacement, TextSpan replacementSpan)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        if (replacementSpan.End > replacement.Text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementSpan),
                replacementSpan,
                $"The span extends beyond the {replacement.Text.Length} characters of the replacement.");
        }

        Validate(span);

        _splices.Add(new Splice(span, replacement, replacementSpan, null));
    }

    /// <summary>
    /// Replaces a range of the source with text the caller wrote, which belongs to no document.
    /// </summary>
    /// <remarks>
    /// For the adjustments splicing forces rather than for content: dropping what a spliced-in
    /// fragment may not keep, or adding what it needs at the top. Every position inside the new
    /// text maps back to where it was put, because there is no character of any document for it
    /// to map to instead.
    /// </remarks>
    /// <param name="span">The range of the source to replace, empty to insert.</param>
    /// <param name="text">The text to put there, empty to delete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> extends beyond the source.</exception>
    /// <exception cref="ArgumentException"><paramref name="span"/> overlaps a replacement already made.</exception>
    public void Replace(TextSpan span, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Validate(span);

        _splices.Add(new Splice(span, null, default, text));
    }

    /// <summary>Builds the projection.</summary>
    /// <returns>
    /// The projection, which is an identity projection when nothing was spliced in. The builder
    /// may be used again afterwards.
    /// </returns>
    public TextProjection ToProjection()
    {
        if (_splices.Count == 0)
        {
            return TextProjection.Identity(_source, _sourceUri);
        }

        _splices.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));

        var text = new StringBuilder(_source.Length);
        ImmutableArray<TextProjectionSegment>.Builder segments =
            ImmutableArray.CreateBuilder<TextProjectionSegment>();

        int consumed = 0;

        foreach (Splice splice in _splices)
        {
            Copy(text, segments, TextSpan.FromBounds(consumed, splice.Span.Start));
            Insert(text, segments, splice);

            consumed = splice.Span.End;
        }

        Copy(text, segments, TextSpan.FromBounds(consumed, _source.Length));

        return new TextProjection(
            _source,
            _sourceUri,
            SourceText.From(text.ToString(), _source.Encoding, _source.HasByteOrderMark),
            segments.ToImmutable(),
            isIdentity: false);
    }

    /// <summary>Checks that a replacement lies within the source and clashes with nothing.</summary>
    private void Validate(TextSpan span)
    {
        if (span.End > _source.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span), span, $"The span extends beyond the {_source.Length} characters of the source.");
        }

        foreach (Splice existing in _splices)
        {
            if (existing.Span.OverlapsWith(span) || (span.IsEmpty && existing.Span.Contains(span.Start)))
            {
                throw new ArgumentException(
                    $"The replacement at {span} overlaps the one already made at {existing.Span}.",
                    nameof(span));
            }
        }
    }

    /// <summary>Copies a run of the source through unchanged.</summary>
    private void Copy(
        StringBuilder text,
        ImmutableArray<TextProjectionSegment>.Builder segments,
        TextSpan span)
    {
        if (span.IsEmpty)
        {
            return;
        }

        segments.Add(new TextProjectionSegment(
            new TextSpan(text.Length, span.Length), span.Start, _sourceUri, isOriginal: true));

        text.Append(_source.GetText(span));
    }

    /// <summary>
    /// Writes a replacement in, carrying its own segments across so that text which reached it
    /// from a third document is still attributed to that document.
    /// </summary>
    private void Insert(
        StringBuilder text,
        ImmutableArray<TextProjectionSegment>.Builder segments,
        Splice splice)
    {
        int start = text.Length;

        if (splice.Replacement is null)
        {
            if (splice.Text!.Length > 0)
            {
                segments.Add(new TextProjectionSegment(
                    new TextSpan(start, splice.Text.Length),
                    new TextSpan(splice.Span.Start, 0),
                    _sourceUri,
                    isOriginal: true,
                    isSynthesized: true));

                text.Append(splice.Text);
            }

            return;
        }

        foreach (TextProjectionSegment segment in splice.Replacement.Segments)
        {
            if (segment.ProjectedSpan.Overlap(splice.ReplacementSpan) is not { } taken)
            {
                continue;
            }

            segments.Add(new TextProjectionSegment(
                new TextSpan(start + (taken.Start - splice.ReplacementSpan.Start), taken.Length),
                segment.SourceSpan.Start + (taken.Start - segment.ProjectedSpan.Start),
                segment.SourceUri,

                // Original to the replacement is still spliced-in here. Only the document being
                // projected is the original one, or a position in an include would come back as
                // an offset into a file it is not in.
                isOriginal: false));
        }

        text.Append(splice.Replacement.Text.GetText(splice.ReplacementSpan));
    }

    /// <summary>
    /// One replacement: either a range of another projection, or text written here.
    /// </summary>
    private readonly record struct Splice(
        TextSpan Span,
        TextProjection? Replacement,
        TextSpan ReplacementSpan,
        string? Text);
}
