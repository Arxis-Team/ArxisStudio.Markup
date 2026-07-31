using System.Collections.Immutable;

namespace ArxisStudio.Markup;

/// <summary>
/// One committed transaction, recorded so it can be undone and redone as a unit.
/// </summary>
/// <param name="Description">The human-readable description the transaction was opened with.</param>
/// <param name="Transitions">Every document the transaction touched, with its text on both sides.</param>
internal sealed record MarkupHistoryEntry(
    string Description,
    ImmutableArray<DocumentTransition> Transitions);
