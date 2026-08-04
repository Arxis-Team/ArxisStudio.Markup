namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Whether a session's objects can still be believed to describe its document.
/// </summary>
/// <remarks>
/// A session is usable for its whole life unless one update writes to a live object and then
/// fails. There is no way back from that — the writes that succeeded were arbitrary user code —
/// so rather than carry on and compound it, the session says so and stops accepting changes.
/// </remarks>
public enum XamlSessionState
{
    /// <summary>
    /// The objects were built from the session's document and every change since has either
    /// landed whole or touched nothing.
    /// </summary>
    Usable,

    /// <summary>
    /// An update failed after it had begun writing, so the objects and the document disagree in
    /// a way nothing here can describe.
    /// </summary>
    /// <remarks>
    /// Reading a session in this state is still allowed, because a caller has to be able to see
    /// what it was holding. Changing one is not: every mutating operation is refused with
    /// <see cref="XamlLoaderDiagnosticCodes.SessionRequiresRecreation"/>. Creating a new session
    /// from the document you wanted is what restores agreement.
    /// </remarks>
    RequiresNewSession,
}
