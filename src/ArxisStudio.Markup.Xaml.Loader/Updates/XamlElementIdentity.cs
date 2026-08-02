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
    /// <remarks>
    /// Content and members are paired separately. A property element — <c>&lt;Grid.Children&gt;</c>,
    /// <c>&lt;Border.Resources&gt;</c> — is a member of its parent rather than a thing beside its
    /// siblings: it produces no object of its own, it cannot be named, and it cannot change places
    /// with a control. Pairing it as though it could is what put an element that produced no
    /// object into an order of objects.
    /// </remarks>
    /// <param name="before">The children as they were, in document order.</param>
    /// <param name="after">The children as they now read, in document order.</param>
    /// <returns>
    /// The pairing, or <see langword="null"/> when identity cannot decide and position has to.
    /// </returns>
    internal static XamlElementPairing? Pair(
        IReadOnlyList<XamlElement> before,
        IReadOnlyList<XamlElement> after)
    {
        XamlElement[] content = [.. before.Where(static element => !element.IsPropertyElementSyntax)];
        XamlElement[] recontent = [.. after.Where(static element => !element.IsPropertyElementSyntax)];
        XamlElement[] members = [.. before.Where(static element => element.IsPropertyElementSyntax)];
        XamlElement[] remembers = [.. after.Where(static element => element.IsPropertyElementSyntax)];

        if (content.Length != recontent.Length || members.Length != remembers.Length)
        {
            return null;
        }

        Dictionary<string, XamlElement>? named = Named(content);
        Dictionary<string, XamlElement>? renamed = Named(recontent);

        if (named is null || renamed is null || named.Count == 0 || !named.Keys.ToHashSet().SetEquals(renamed.Keys))
        {
            // No names to go on, a name used twice, or a set of names that is not the same set:
            // in none of those cases does a name say which element is which.
            return null;
        }

        XamlElement[] anonymous = [.. content.Where(element => Of(element) is null)];
        var pairs = new List<(XamlElement Before, XamlElement After)>(recontent.Length);
        int next = 0;

        foreach (XamlElement element in recontent)
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

        return new XamlElementPairing
        {
            Content = pairs,

            // Members keep their order because there is no order for them to keep: which member
            // a property element is, is its name, and that is compared where its content is.
            Members = [.. members.Zip(remembers)],
        };
    }

    /// <summary>
    /// Reports whether a pairing puts anything anywhere other than where it already was.
    /// </summary>
    /// <param name="before">The children as they were, in document order.</param>
    /// <param name="pairing">The pairing.</param>
    /// <returns><see langword="true"/> when the order changed.</returns>
    internal static bool Moved(IReadOnlyList<XamlElement> before, XamlElementPairing pairing)
    {
        XamlElement[] content = [.. before.Where(static element => !element.IsPropertyElementSyntax)];

        for (int index = 0; index < pairing.Content.Count; index++)
        {
            if (!ReferenceEquals(content[index], pairing.Content[index].Before))
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
