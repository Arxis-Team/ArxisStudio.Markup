using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// Replacement of the characters in <paramref name="Span"/> with <paramref name="NewText"/>.
/// </summary>
/// <remarks>
/// A change is the unit the whole library edits with. Preserving unrelated source text is a
/// primary requirement of this project, so callers should describe the smallest region that
/// actually changes rather than replacing a whole element or document.
/// </remarks>
/// <param name="Span">The range of existing text to replace, which may be empty for an insertion.</param>
/// <param name="NewText">The replacement text, which may be empty for a deletion.</param>
public readonly record struct TextChange(TextSpan Span, string NewText)
{
    /// <summary>Gets the replacement text.</summary>
    public string NewText { get; } = NewText ?? throw new ArgumentNullException(nameof(NewText));

    /// <summary>Gets a value indicating whether the change inserts text without removing any.</summary>
    public bool IsInsertion => Span.IsEmpty && NewText.Length > 0;

    /// <summary>Gets a value indicating whether the change removes text without adding any.</summary>
    public bool IsDeletion => !Span.IsEmpty && NewText.Length == 0;

    /// <summary>
    /// Gets the change in total text length this change produces, which is negative when the
    /// change removes more than it adds.
    /// </summary>
    public int Delta => NewText.Length - Span.Length;

    /// <summary>Creates a change that inserts text at a position without removing any.</summary>
    /// <param name="position">The zero-based offset to insert at.</param>
    /// <param name="text">The text to insert.</param>
    /// <returns>The insertion.</returns>
    public static TextChange Insert(int position, string text) =>
        new(new TextSpan(position, 0), text);

    /// <summary>Creates a change that removes the characters in a span.</summary>
    /// <param name="span">The range to remove.</param>
    /// <returns>The deletion.</returns>
    public static TextChange Delete(TextSpan span) => new(span, string.Empty);

    /// <summary>Returns a string of the form <c>[start..end) -> "text"</c>.</summary>
    /// <returns>A readable representation of the change.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Span} -> \"{NewText}\"");
}
