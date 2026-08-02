using System.Collections.Generic;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Which element of a changed document stands for which element of the one before it.
/// </summary>
/// <remarks>
/// Content and members are kept apart because only one of them can be reordered. A control can
/// change places with its siblings; <c>&lt;Grid.ColumnDefinitions&gt;</c> cannot change places
/// with anything, produces no object, and has no business in a list of objects to move.
/// </remarks>
internal sealed class XamlElementPairing
{
    /// <summary>Gets the paired children that produce objects, in the new document's order.</summary>
    internal required IReadOnlyList<(XamlElement Before, XamlElement After)> Content { get; init; }

    /// <summary>Gets the paired property elements, in the order both documents write them.</summary>
    internal required IReadOnlyList<(XamlElement Before, XamlElement After)> Members { get; init; }

    /// <summary>Gets every pair, whatever kind of child it is.</summary>
    internal IEnumerable<(XamlElement Before, XamlElement After)> All => Content.Concat(Members);
}
