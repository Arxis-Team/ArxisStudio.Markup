using System;

namespace ArxisStudio.Markup;

/// <summary>
/// What one document looked like before and after a committed transaction.
/// </summary>
/// <remarks>
/// <para>
/// Only the text is recorded, never a whole snapshot. Undo re-applies the earlier text as a
/// <em>new</em> snapshot at a version that has never been used, rather than restoring the old
/// snapshot object with its old version.
/// </para>
/// <para>
/// That distinction matters more than it looks. Later milestones cache parse trees against a
/// document version, and every edit validates that the nodes it targets came from the version
/// it believes it is editing. If undo put version 4 back, a subsequent different edit would
/// produce a second, different version 4, and a node captured against the first would pass the
/// version check while carrying spans that no longer fit the text. Never reusing a version
/// makes that failure impossible instead of merely unlikely.
/// </para>
/// </remarks>
/// <param name="Id">The document's identity, which is stable across the transition.</param>
/// <param name="Uri">The document's location.</param>
/// <param name="Before">The text before the transaction, or <see langword="null"/> if it did not exist.</param>
/// <param name="After">The text after the transaction, or <see langword="null"/> if it was removed.</param>
internal sealed record DocumentTransition(
    MarkupDocumentId Id,
    Uri Uri,
    SourceText? Before,
    SourceText? After);
