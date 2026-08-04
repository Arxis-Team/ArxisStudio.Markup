using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What an attempt to bring a session's objects in line with a changed document did.
/// </summary>
/// <remarks>
/// <para>
/// An update lands whole, refuses without touching anything, or stops part-way — and which of the
/// three it was is <see cref="Outcome"/>. Only the first two leave a session worth keeping.
/// </para>
/// <para>
/// Everything that can be checked is checked before the first live write, so a document caught
/// mid-keystroke, a value a member cannot hold and a fragment that will not build all refuse
/// cleanly. What cannot be checked in advance is user code: a validating setter that refuses a
/// value of the right type, or a collection that will not take back what was moved out of it. One
/// of those failing after an earlier write has already landed is
/// <see cref="XamlUpdateOutcome.RequiresNewSession"/>, because there is no honest way to describe
/// what the objects now hold and no safe way to undo arbitrary side effects.
/// </para>
/// <para>
/// <see cref="Strategy"/> is the other axis and answers a different question: what the change
/// would have needed, whether or not it happened. A rejection carrying
/// <see cref="XamlUpdateStrategy.RecreateSession"/> touched nothing and left the session usable;
/// it is the *document* that cannot be reached from here.
/// </para>
/// </remarks>
public sealed class XamlUpdateResult
{
    /// <summary>Gets what the update did to the objects.</summary>
    public required XamlUpdateOutcome Outcome { get; init; }

    /// <summary>Gets the largest strategy the update called for.</summary>
    public required XamlUpdateStrategy Strategy { get; init; }

    /// <summary>Gets the changes the comparison found, in document order.</summary>
    public required ImmutableArray<XamlDocumentChange> Changes { get; init; }

    /// <summary>Gets everything noticed on the way.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }

    /// <summary>
    /// Gets a value indicating whether the objects and the session's document moved on.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Outcome"/> rather than set beside it, so the two can never
    /// disagree. A caller that only wants to know whether to redraw can read this; one that has
    /// to decide whether to keep the session must read <see cref="Outcome"/>, because both kinds
    /// of failure read <see langword="false"/> here and only one of them is recoverable.
    /// </remarks>
    public bool Applied => Outcome == XamlUpdateOutcome.Applied;
}
