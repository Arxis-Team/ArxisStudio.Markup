using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Says that a <see cref="XamlLiveDocument"/> has put a different session — or none — where its
/// session was.
/// </summary>
/// <remarks>
/// <para>
/// Raised on the thread that owns the objects, while the previous session is still open: its root
/// and its map are what a host takes off its canvas, and a window it built is what the host closes.
/// The previous session is disposed once every handler has returned.
/// </para>
/// <para>
/// The objects are the host's, and nothing here tears them down — a host may want to keep showing
/// them until the new ones are in place.
/// </para>
/// </remarks>
public sealed class XamlSessionReplacedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="previous">The session that was in place, or <see langword="null"/>.</param>
    /// <param name="current">The session now in place, or <see langword="null"/>.</param>
    public XamlSessionReplacedEventArgs(XamlLoadSession? previous, XamlLoadSession? current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>Gets the session that was in place, or <see langword="null"/> when there was none.</summary>
    public XamlLoadSession? Previous { get; }

    /// <summary>Gets the session now in place, or <see langword="null"/> when nothing shows the document.</summary>
    public XamlLoadSession? Current { get; }
}
