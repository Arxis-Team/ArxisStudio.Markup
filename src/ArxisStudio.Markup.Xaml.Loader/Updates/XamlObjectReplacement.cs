using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Puts a rebuilt object back where the one it replaces was sitting.
/// </summary>
/// <remarks>
/// <para>
/// The document says where that is. An element's parent is either a property element, which
/// names the member the object belongs to, or an ordinary element, whose object holds it as
/// content — and from there it is a dictionary entry, a position in a list, or a property.
/// </para>
/// <para>
/// Nothing here guesses. A slot it cannot identify is reported, and the update that needed it is
/// refused, because putting a rebuilt style in the wrong place is worse than not updating.
/// </para>
/// </remarks>
internal static class XamlObjectReplacement
{
    /// <summary>Replaces the object an element produced with a freshly built one.</summary>
    /// <param name="objects">Which element each object came from.</param>
    /// <param name="element">The element whose object is being replaced.</param>
    /// <param name="previous">The object to replace.</param>
    /// <param name="fresh">The object to put in its place.</param>
    /// <param name="members">What decides which member of the holder the element sits in.</param>
    /// <param name="diagnostics">Collects a report when there is nowhere to put it.</param>
    /// <returns><see langword="true"/> if the object was replaced.</returns>
    internal static bool Replace(
        XamlObjectMap objects,
        XamlElement element,
        object previous,
        object fresh,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        if (Owner(objects, element) is not var (owner, memberName))
        {
            return Fail(element, diagnostics, "nothing in this document holds it");
        }

        if (owner is null)
        {
            return Fail(element, diagnostics, "the element that holds it produced no object");
        }

        object? slot = memberName is null ? owner : Read(owner, memberName, members);

        if (slot is IResourceDictionary dictionary && ReplaceInDictionary(dictionary, previous, fresh))
        {
            return true;
        }

        if (slot is not null && ReplaceInList(slot, previous, fresh))
        {
            return true;
        }

        // A single-valued slot: a Template, a Content, a Child. Setting it is the replacement.
        if (memberName is not null)
        {
            return Write(owner, memberName, fresh, members, diagnostics)
                || Fail(element, diagnostics, $"{memberName} could not be written");
        }

        // Children is where a panel's unnamed content goes, and it is a property rather than the
        // panel itself, so reaching it takes knowing that about a panel.
        if (owner is Panel panel && ReplaceInList(panel.Children, previous, fresh))
        {
            return true;
        }

        if (owner is ContentControl content && ReferenceEquals(content.Content, previous))
        {
            content.Content = fresh;

            return true;
        }

        if (owner is Decorator decorator && ReferenceEquals(decorator.Child, previous))
        {
            decorator.Child = fresh as Control;

            return true;
        }

        return Fail(element, diagnostics, $"{owner.GetType().Name} does not say where it holds it");
    }

    /// <summary>Puts an element's children in the order the document now gives them.</summary>
    /// <remarks>
    /// <para>
    /// Nothing is built and nothing is detached: the objects that already exist are moved within
    /// the collection holding them, so a control keeps its focus, its scroll offset, whatever it
    /// was animating and anything a caller was holding it for.
    /// </para>
    /// <para>
    /// Everything that can fail is resolved before the first item moves, so a reorder that cannot
    /// be applied has not half-applied itself.
    /// </para>
    /// </remarks>
    /// <param name="objects">Which element each object came from.</param>
    /// <param name="parent">The element whose children changed places.</param>
    /// <param name="order">Its children as they were, in the order the new document gives them.</param>
    /// <param name="members">What decides which member holds the children.</param>
    /// <param name="diagnostics">Collects a report when the order cannot be applied.</param>
    /// <returns><see langword="true"/> if the children were reordered.</returns>
    internal static bool Reorder(
        XamlObjectMap objects,
        XamlElement parent,
        IReadOnlyList<XamlElement> order,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        if (Children(objects, parent, members) is not { } slot)
        {
            return FailOrder(parent, diagnostics, "nothing in this document holds them");
        }

        var wanted = new List<object>(order.Count);

        foreach (XamlElement child in order)
        {
            if (objects.GetObject(child) is not { } target)
            {
                return FailOrder(parent, diagnostics, $"<{child.Name}> produced no object");
            }

            wanted.Add(target);
        }

        return Rearrange(slot, wanted)
            || FailOrder(parent, diagnostics, $"{slot.GetType().Name} does not say how to move an item");
    }

    /// <summary>Finds the collection an element's children live in.</summary>
    private static object? Children(XamlObjectMap objects, XamlElement parent, XamlMemberResolver members)
    {
        // A property element names the member the children belong to; the object that has that
        // member is its own parent.
        if (parent.IsPropertyElementSyntax)
        {
            return parent.Parent is XamlElement owner
                && parent.MemberName is { } memberName
                && objects.GetObject(owner) is { } target
                    ? Read(target, memberName, members)
                    : null;
        }

        return objects.GetObject(parent) switch
        {
            // Children is where a panel's unnamed content goes, and it is a property rather than
            // the panel itself.
            Panel panel => panel.Children,
            { } holder => holder,
            _ => null,
        };
    }

    /// <summary>Moves the items of a collection into a given order, without removing any.</summary>
    /// <remarks>
    /// Through the collection's own <c>Move</c>, which is what Avalonia's lists offer and what
    /// keeps a control attached to its parent throughout. Writing the positions instead would put
    /// one control in two places for as long as it took to write the second, which is how Avalonia
    /// is made to throw.
    /// </remarks>
    private static bool Rearrange(object slot, List<object> order)
    {
        if (slot is not IEnumerable items)
        {
            return false;
        }

        var current = new List<object?>();

        foreach (object? item in items)
        {
            current.Add(item);
        }

        // Anything the collection holds that the document does not declare would have to end up
        // somewhere, and nothing here knows where. That is a reorder this cannot promise.
        if (current.Count != order.Count)
        {
            return false;
        }

        MethodInfo? move = slot.GetType().GetMethod(
            "Move", BindingFlags.Public | BindingFlags.Instance, null, [typeof(int), typeof(int)], null);

        if (move is null)
        {
            return false;
        }

        var positions = new int[order.Count];

        for (int target = 0; target < order.Count; target++)
        {
            positions[target] = current.FindIndex(item => ReferenceEquals(item, order[target]));

            if (positions[target] < 0)
            {
                return false;
            }
        }

        // Resolved above, applied here: by this point nothing left can refuse.
        for (int target = 0; target < order.Count; target++)
        {
            int position = current.FindIndex(target, item => ReferenceEquals(item, order[target]));

            if (position == target)
            {
                continue;
            }

            move.Invoke(slot, [position, target]);

            object? item = current[position];

            current.RemoveAt(position);
            current.Insert(target, item);
        }

        return true;
    }

    /// <summary>Rebuilds what an element holds without disturbing the object it is.</summary>
    /// <remarks>
    /// For a change to an element's content rather than to the element itself. The object stays,
    /// so a caller holding it — or a session built around it, which is the case at the root —
    /// keeps working, and only what is inside is built again.
    /// </remarks>
    /// <param name="target">The object whose content is being rebuilt.</param>
    /// <param name="fresh">A freshly built copy of the same element.</param>
    /// <param name="element">The element, for diagnostics.</param>
    /// <param name="diagnostics">Collects a report when the content cannot be moved across.</param>
    /// <returns><see langword="true"/> if the content was rebuilt.</returns>
    internal static bool ReplaceContent(
        object target,
        object fresh,
        XamlElement element,
        List<MarkupDiagnostic> diagnostics)
    {
        if (target.GetType() != fresh.GetType())
        {
            return Fail(element, diagnostics, "the rebuilt object is not the same kind of object");
        }

        switch (target)
        {
            case Panel panel when fresh is Panel rebuilt:
                Control[] children = [.. rebuilt.Children];

                // Detached from the copy first: a control belongs to one parent, and adding it
                // to a second while the first still holds it is how Avalonia is made to throw.
                rebuilt.Children.Clear();
                panel.Children.Clear();
                panel.Children.AddRange(children);

                return true;

            case ContentControl control when fresh is ContentControl rebuilt:
                object? value = rebuilt.Content;
                rebuilt.Content = null;
                control.Content = value;

                return true;

            case Decorator decorator when fresh is Decorator rebuilt:
                Control? child = rebuilt.Child;
                rebuilt.Child = null;
                decorator.Child = child;

                return true;

            case IResourceDictionary dictionary when fresh is IResourceDictionary rebuilt:
                dictionary.Clear();

                foreach (object key in rebuilt.Keys.ToArray())
                {
                    dictionary[key] = rebuilt[key];
                }

                // Merged dictionaries are not entries. A file that only merges other files has
                // all of its content there and none of it under a key, so copying the keys alone
                // would leave the old content in place and call it an update.
                IResourceProvider[] merged = [.. rebuilt.MergedDictionaries];

                dictionary.MergedDictionaries.Clear();

                // Taken out of the copy first, for the same reason a control is: a provider that
                // two dictionaries both hold has an owner that is no longer true.
                rebuilt.MergedDictionaries.Clear();

                foreach (IResourceProvider provider in merged)
                {
                    dictionary.MergedDictionaries.Add(provider);
                }

                return true;

            default:
                return Fail(element, diagnostics, $"{target.GetType().Name} does not say what it holds");
        }
    }

    /// <summary>
    /// Finds the object that holds an element's object, and the member it holds it under.
    /// </summary>
    private static (object? Owner, string? MemberName)? Owner(XamlObjectMap objects, XamlElement element)
    {
        if (element.Parent is not XamlElement parent)
        {
            return null;
        }

        // A property element names the member; the object that has that member is its own parent.
        return parent.IsPropertyElementSyntax
            ? parent.Parent is XamlElement grandparent
                ? (objects.GetObject(grandparent), parent.MemberName)
                : null
            : (objects.GetObject(parent), null);
    }

    private static bool ReplaceInDictionary(IResourceDictionary dictionary, object previous, object fresh)
    {
        foreach (object key in dictionary.Keys.ToArray())
        {
            if (ReferenceEquals(dictionary[key], previous))
            {
                dictionary[key] = fresh;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces an item of a collection, by reference, whichever list interface it offers.
    /// </summary>
    /// <remarks>
    /// Avalonia's own collections — <c>Styles</c>, <c>Controls</c>, a dictionary's merged list —
    /// implement <see cref="IList{T}"/> and not the non-generic <see cref="IList"/>, so testing
    /// for the latter alone finds none of the ones this actually has to put things back into.
    /// </remarks>
    private static bool ReplaceInList(object slot, object previous, object fresh)
    {
        if (slot is not IEnumerable items)
        {
            return false;
        }

        int index = 0;

        foreach (object? item in items)
        {
            if (ReferenceEquals(item, previous))
            {
                return Set(slot, index, fresh);
            }

            index++;
        }

        return false;
    }

    /// <summary>Writes one position of a collection through its indexer.</summary>
    private static bool Set(object slot, int index, object value)
    {
        if (slot is IList list)
        {
            list[index] = value;

            return true;
        }

        PropertyInfo? indexer = slot.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property =>
                property.CanWrite && property.GetIndexParameters() is [{ ParameterType.Name: nameof(Int32) }]);

        if (indexer is null)
        {
            return false;
        }

        indexer.SetValue(slot, value, [index]);

        return true;
    }

    private static object? Read(object owner, string memberName, XamlMemberResolver members)
    {
        XamlMemberDescriptor member = members.Resolve(owner.GetType(), memberName);

        return member switch
        {
            { AvaloniaProperty: { } property } when owner is Avalonia.AvaloniaObject avaloniaObject =>
                avaloniaObject.GetValue(property),
            { ClrProperty.CanRead: true } => member.ClrProperty!.GetValue(owner),
            _ => null,
        };
    }

    private static bool Write(
        object owner,
        string memberName,
        object? value,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        XamlMemberDescriptor member = members.Resolve(owner.GetType(), memberName);

        if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
        {
            return false;
        }

        try
        {
            XamlDesignValues.Write(owner, member, value);

            return true;
        }
        catch (Exception error) when (error is InvalidCastException or ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.IncompatibleValue,
                $"The rebuilt value could not be assigned to {owner.GetType().Name}.{memberName}: {error.Message}",
                MarkupDiagnosticSeverity.Error));

            return false;
        }
    }

    private static bool FailOrder(XamlElement element, List<MarkupDiagnostic> diagnostics, string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.UpdateNotApplied,
            $"The children of <{element.Name}> could not be put in the order the document gives them: {reason}.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return false;
    }

    private static bool Fail(XamlElement element, List<MarkupDiagnostic> diagnostics, string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.UpdateNotApplied,
            $"<{element.Name}> was rebuilt but could not be put back: {reason}.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return false;
    }
}
