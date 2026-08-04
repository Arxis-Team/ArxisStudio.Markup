namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What an update did to the objects, which is a different question from what it was trying to do.
/// </summary>
/// <remarks>
/// <para>
/// Two things a caller needs to know about a failed update, and a single flag can only carry one:
/// whether the document moved, and whether the objects can still be believed. This answers the
/// second. <see cref="XamlUpdateResult.Strategy"/> answers what the change would have needed.
/// </para>
/// <para>
/// Nothing here promises to undo a change. Property setters, type converters, markup extensions,
/// collection mutations and custom controls run user code with side effects nothing can reverse
/// on its behalf, so what is offered instead is an honest report of how far the attempt got.
/// </para>
/// </remarks>
public enum XamlUpdateOutcome
{
    /// <summary>
    /// The objects and the session's document both moved to the new state.
    /// </summary>
    Applied,

    /// <summary>
    /// Nothing was written to a live object, and the session is exactly as usable as it was.
    /// </summary>
    /// <remarks>
    /// The ordinary failure, and the common one: a document caught halfway through being typed,
    /// a value the member cannot hold, a fragment that will not build. Everything that can be
    /// checked is checked before the first write, so most refusals land here — and the next
    /// keystroke is usually the correction.
    /// </remarks>
    RejectedCleanly,

    /// <summary>
    /// Something was written to a live object before the update failed, so the objects agree with
    /// neither document and this session cannot be trusted for further work.
    /// </summary>
    /// <remarks>
    /// The session refuses every later mutation rather than compounding the disagreement, and
    /// <see cref="XamlLoadSession.State"/> reads
    /// <see cref="XamlSessionState.RequiresNewSession"/> from then on. Build a new session from
    /// the document you wanted — <see cref="XamlLoadSession.PendingDocument"/> is that document —
    /// and discard this one.
    /// </remarks>
    RequiresNewSession,
}
