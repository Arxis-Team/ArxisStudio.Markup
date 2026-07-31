using System;
using System.Globalization;

namespace ArxisStudio.Markup;

/// <summary>
/// A monotonically increasing version stamp for a document's text.
/// </summary>
/// <remarks>
/// Versions let an edit assert that the nodes it targets came from the snapshot it believes it
/// is editing. Applying a change computed against a stale version would silently corrupt
/// unrelated source text, which this project treats as the failure to avoid above all others.
/// </remarks>
/// <param name="Value">The underlying counter, which never decreases for a given document.</param>
public readonly record struct DocumentVersion(long Value) : IComparable<DocumentVersion>
{
    /// <summary>Gets the version a newly opened document starts at.</summary>
    public static DocumentVersion Initial => new(0);

    /// <summary>Returns the next version in sequence.</summary>
    /// <returns>The successor of this version.</returns>
    public DocumentVersion Next() => new(Value + 1);

    /// <summary>Compares two versions by age.</summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns>A negative value, zero, or a positive value as this version is older, the same, or newer.</returns>
    public int CompareTo(DocumentVersion other) => Value.CompareTo(other.Value);

    /// <summary>Determines whether the left version is older than the right one.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is older.</returns>
    public static bool operator <(DocumentVersion left, DocumentVersion right) => left.Value < right.Value;

    /// <summary>Determines whether the left version is not newer than the right one.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is older or the same.</returns>
    public static bool operator <=(DocumentVersion left, DocumentVersion right) => left.Value <= right.Value;

    /// <summary>Determines whether the left version is newer than the right one.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is newer.</returns>
    public static bool operator >(DocumentVersion left, DocumentVersion right) => left.Value > right.Value;

    /// <summary>Determines whether the left version is not older than the right one.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is newer or the same.</returns>
    public static bool operator >=(DocumentVersion left, DocumentVersion right) => left.Value >= right.Value;

    /// <summary>Returns the version number.</summary>
    /// <returns>A readable representation of the version.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
