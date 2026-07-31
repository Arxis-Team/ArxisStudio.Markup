using System;

namespace ArxisStudio.Markup;

/// <summary>
/// The stable identity of a document within a <see cref="MarkupWorkspace"/>.
/// </summary>
/// <remarks>
/// Identity is deliberately separate from both the document's URI and its text. A document
/// keeps its identity across every edit, and an unsaved in-memory document keeps it even
/// though no file with that URI exists. Consumers may therefore hold an identifier across
/// arbitrarily many snapshots.
/// </remarks>
/// <param name="Value">The underlying globally unique value.</param>
public readonly record struct MarkupDocumentId(Guid Value)
{
    /// <summary>Creates a new, unique document identity.</summary>
    /// <returns>The new identity.</returns>
    public static MarkupDocumentId New() => new(Guid.NewGuid());

    /// <summary>Returns the underlying value in its standard form.</summary>
    /// <returns>A readable representation of the identity.</returns>
    public override string ToString() => Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}
