using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that keeps the root's name scope naming what the document names.
/// </summary>
/// <remarks>
/// <para>
/// A load registers every name of the document's own scope in a scope it puts on the root, and completes it: Avalonia
/// takes no registration after that and takes none back. An update that builds a named element again, writes a name or
/// takes one off left that scope naming objects the tree no longer holds and missing the ones it does — a binding
/// written in place found nothing by the new name, and code asking the root for a control by its name was handed the
/// one an update had replaced (ADR 0036).
/// </para>
/// <para>
/// So once an update's objects are written and the map follows them, a root whose names moved is given a scope of its
/// own: every name of the document's scope with the object the map now pairs its element with, completed, as a load
/// leaves one. A binding the update wrote in place that reads an element by its name was given that same scope before
/// it was filled, and finds the element as it is registered — the name the same update wrote among them.
/// </para>
/// </remarks>
public sealed partial class XamlLoadSession
{
    /// <summary>The name scope the load left on the root, kept for as long as the session.</summary>
    /// <remarks>
    /// A binding holds its scope weakly, and once an update puts another scope on the root nothing else may hold this
    /// one. A style the load built makes its bindings afresh each time it applies to an element, and one made after
    /// the scope was let go would find no scope at all.
    /// </remarks>
    private readonly INameScope? _loadedNames;

    /// <summary>Gets each name of a document's own scope with the object the map pairs its element with.</summary>
    /// <param name="document">The document the map describes.</param>
    private List<(string Name, object Target)> Named(XamlDocument document) =>
    [
        .. XamlNameScopes.OfDocument(document)
            .Select(name => (name.Name, Target: Objects.GetObject(name.Element)))
            .Where(static name => name.Target is not null)
            .Select(static name => (name.Name, name.Target!)),
    ];

    /// <summary>
    /// Puts on the root a scope naming what the document names now — when the update moved a name, or gave a binding
    /// the scope to find one in.
    /// </summary>
    /// <param name="before">The names as the document the update started from had them.</param>
    /// <param name="handedOut">The scope the update's bindings were given, or <see langword="null"/> when none was.</param>
    private void Rename(List<(string Name, object Target)> before, NameScope? handedOut)
    {
        List<(string Name, object Target)> now = Named(Document);
        StyledElement? root = RootObject as StyledElement;
        INameScope? current = root is null ? null : NameScope.GetNameScope(root);

        bool moved = before.Count != now.Count
            || now.Exists(name => !ReferenceEquals(current?.Find(name.Name), name.Target));

        if (handedOut is null && !moved)
        {
            return;
        }

        NameScope scope = handedOut ?? new NameScope();

        foreach ((string name, object target) in now)
        {
            scope.Register(name, target);
        }

        scope.Complete();

        // A root that is not a styled element has no scope to be given; the bindings the update gave this one have
        // found what they read in it all the same.
        if (root is not null)
        {
            NameScope.SetNameScope(root, scope);
        }
    }
}
