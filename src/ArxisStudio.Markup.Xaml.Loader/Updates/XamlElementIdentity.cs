using System;
using System.Collections.Generic;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Decides which element of a changed document stands for which element of the one before it.
/// </summary>
/// <remarks>
/// <para>
/// Position among siblings is the fallback, not the rule. An author who writes <c>x:Name</c> has
/// said which element this is, and that survives being moved; pairing on it is what lets a control
/// that was dragged up the file keep the object it already had, with its focus, its scroll offset
/// and everything else it was holding.
/// </para>
/// <para>
/// Where a name cannot decide — none declared, one declared twice, a child added or removed — this
/// says so rather than guessing, and the caller falls back to position. Being wrong here would
/// give a control the value of whatever used to sit in its place, which is the one outcome worth
/// being slow to avoid.
/// </para>
/// </remarks>
internal static class XamlElementIdentity
{
    /// <summary>Gets the identity an element declares, if it declares one.</summary>
    /// <remarks>
    /// <c>x:Name</c> first, then <c>Name</c>, which Avalonia treats as the same thing on anything
    /// that has it. A name written as an expression is not an identity: what it stands for is
    /// decided while the objects are being built, and two documents cannot be compared on it.
    /// </remarks>
    /// <param name="element">The element to read.</param>
    /// <returns>The identity, or <see langword="null"/> when the element declares none.</returns>
    internal static string? Of(XamlElement element)
    {
        if (element.GetDirective("Name") is { } directive)
        {
            return directive;
        }

        XamlAttribute? attribute = element.GetAttribute(XamlQualifiedName.Parse("Name"));

        return attribute?.GetValue() is XamlLiteralValue ? attribute.GetValueText() : null;
    }

    /// <summary>
    /// Pairs two versions of one element's children, by identity where there is one and by
    /// position otherwise.
    /// </summary>
    /// <param name="before">The children as they were, in document order.</param>
    /// <param name="after">The children as they now read, in document order.</param>
    /// <returns>
    /// The pairs, in the new document's order, or <see langword="null"/> when identity cannot
    /// decide and position has to.
    /// </returns>
    internal static IReadOnlyList<(XamlElement Before, XamlElement After)>? Pair(
        IReadOnlyList<XamlElement> before,
        IReadOnlyList<XamlElement> after)
    {
        if (before.Count != after.Count)
        {
            return null;
        }

        Dictionary<string, XamlElement>? named = Named(before);
        Dictionary<string, XamlElement>? renamed = Named(after);

        if (named is null || renamed is null || named.Count == 0 || !named.Keys.ToHashSet().SetEquals(renamed.Keys))
        {
            // No names to go on, a name used twice, or a set of names that is not the same set:
            // in none of those cases does a name say which element is which.
            return null;
        }

        XamlElement[] anonymous = [.. before.Where(element => Of(element) is null)];
        var pairs = new List<(XamlElement Before, XamlElement After)>(after.Count);
        int next = 0;

        foreach (XamlElement element in after)
        {
            if (Of(element) is { } identity)
            {
                pairs.Add((named[identity], element));
            }
            else
            {
                // Nothing names it, so the only thing it can be paired with is whatever unnamed
                // element stood in the same place among the other unnamed ones.
                pairs.Add((anonymous[next++], element));
            }
        }

        return pairs;
    }

    /// <summary>
    /// Reports whether a pairing puts anything anywhere other than where it already was.
    /// </summary>
    /// <param name="before">The children as they were, in document order.</param>
    /// <param name="pairs">The pairing, in the new document's order.</param>
    /// <returns><see langword="true"/> when the order changed.</returns>
    internal static bool Moved(
        IReadOnlyList<XamlElement> before,
        IReadOnlyList<(XamlElement Before, XamlElement After)> pairs)
    {
        for (int index = 0; index < pairs.Count; index++)
        {
            if (!ReferenceEquals(before[index], pairs[index].Before))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Indexes the elements that declare an identity, or reports that one is used twice.</summary>
    private static Dictionary<string, XamlElement>? Named(IReadOnlyList<XamlElement> elements)
    {
        var named = new Dictionary<string, XamlElement>(StringComparer.Ordinal);

        foreach (XamlElement element in elements)
        {
            if (Of(element) is not { } identity)
            {
                continue;
            }

            if (!named.TryAdd(identity, element))
            {
                return null;
            }
        }

        return named;
    }
}
