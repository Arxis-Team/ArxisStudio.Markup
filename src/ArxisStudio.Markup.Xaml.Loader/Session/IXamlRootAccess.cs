using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Gives a session the whole of a root a host has borrowed parts of, for exactly as long as the
/// session writes to it.
/// </summary>
/// <remarks>
/// <para>
/// A host that shows a window cannot put the window anywhere: a top level is the content of
/// nothing. What it shows instead is the window's content, its resources and its styles, taken out
/// of the window and put into something that can be shown. While they are out, the window the
/// session is built around is empty — an update writing the window's rebuilt content would write
/// it into a window nobody is looking at, and rebuilding the map by walking the window's children
/// would find none of them.
/// </para>
/// <para>
/// So every write a session makes to its objects happens while the root is lent back: an update —
/// the writes, the map rebuilt over them and the design values applied again, all in one turn of
/// the dispatcher, so nothing renders a root half given back — and every synchronous edit. The
/// lease is disposed when the write is over, also when it failed, and the host borrows the parts
/// again from what the root holds then, which may be what the write has just rebuilt.
/// </para>
/// <para>
/// Called on the thread that owns the objects, and never while another lease from the same session
/// is open.
/// </para>
/// </remarks>
public interface IXamlRootAccess
{
    /// <summary>Gives back to the root whatever has been borrowed from it.</summary>
    /// <param name="root">The session's root object.</param>
    /// <returns>The lease, whose disposal lets the host borrow again.</returns>
    IDisposable Lend(object root);
}
