using System;
using System.Collections.Generic;
using System.Text;

namespace ArxisStudio.Markup;

/// <summary>
/// A <see cref="SourceText"/> backed by a single string.
/// </summary>
/// <remarks>
/// Kept internal so the public surface stays the abstraction. Documents in this library are
/// measured in kilobytes to a few megabytes, where a flat string beats a rope for both read
/// speed and simplicity; if very large documents with frequent edits ever become a real
/// scenario, a segmented implementation can be added behind the same abstraction without a
/// public API change.
/// </remarks>
internal sealed class StringSourceText : SourceText
{
    private readonly string _text;

    public StringSourceText(string text, Encoding encoding, bool hasByteOrderMark)
        : base(encoding, hasByteOrderMark) => _text = text;

    public override int Length => _text.Length;

    public override char this[int index] => _text[index];

    public override string GetText(TextSpan span)
    {
        if (span.End > _text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                span,
                $"The span extends beyond the {_text.Length} characters of the snapshot.");
        }

        return span.Length == 0 ? string.Empty : _text.Substring(span.Start, span.Length);
    }

    public override ReadOnlySpan<char> ToCharSpan() => _text.AsSpan();

    public override SourceText WithChanges(IReadOnlyList<TextChange> changes)
    {
        ValidateChanges(changes, _text.Length);

        if (changes.Count == 0)
        {
            return this;
        }

        int length = _text.Length;

        foreach (TextChange change in changes)
        {
            length += change.Delta;
        }

        var builder = new StringBuilder(length);
        int position = 0;

        foreach (TextChange change in changes)
        {
            // Everything between the previous change and this one is carried over verbatim.
            builder.Append(_text, position, change.Span.Start - position);
            builder.Append(change.NewText);
            position = change.Span.End;
        }

        builder.Append(_text, position, _text.Length - position);

        return new StringSourceText(builder.ToString(), Encoding, HasByteOrderMark);
    }

    public override string ToString() => _text;
}
