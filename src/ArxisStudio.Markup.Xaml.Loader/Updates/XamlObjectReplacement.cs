using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;

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
/// <para>
/// Every step says whether it refused before touching anything or stopped part-way through
/// changing it. The difference is the whole of what a caller can do next: a refusal leaves a
/// session that still describes its document, and a half-made change leaves one that describes
/// nothing. Collections are the reason the distinction is not obvious — moving content out of a
/// rebuilt copy and into the original empties one before it fills the other, and a failure
/// between the two is not a refusal however it is reported.
/// </para>
/// <para>
/// The rule for deciding between them: <b>a refusal has to be reached without running the
/// object's own code.</b> Ask first — is the member writable, does the collection say it is
/// read-only — and refuse on the answer. Once a setter, an accessor or a collection method has
/// actually been called and thrown, what it did before throwing is unknowable, and looking at the
/// property afterwards proves nothing about the rest of the object.
/// </para>
/// <para>
/// The exception is a write to a rebuilt copy. Those are built by this update and have never been
/// handed to anybody, so whatever a failing setter did to one goes out with it.
/// <see cref="XamlObjectExposure"/> is which of the two a write is against.
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
    /// <returns>Whether the object was replaced, refused, or left part-way.</returns>
    internal static XamlMutationOutcome Replace(
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

        if (slot is IResourceDictionary dictionary
            && ReplaceInDictionary(dictionary, previous, fresh) is { } inDictionary)
        {
            return Report(element, diagnostics, inDictionary, "the dictionary would not take it");
        }

        if (slot is not null && ReplaceInList(slot, previous, fresh) is { } inList)
        {
            return Report(element, diagnostics, inList, $"{slot.GetType().Name} would not take it");
        }

        // A single-valued slot: a Template, a Content, a Child. Setting it is the replacement.
        if (memberName is not null)
        {
            return Report(
                element,
                diagnostics,
                Write(owner, memberName, fresh, members, diagnostics, XamlObjectExposure.Live),
                $"{memberName} could not be written");
        }

        // Unnamed content goes to the member Avalonia marks [Content] — Children on a panel,
        // Content on a content control, Child on a decorator, and whatever a control library
        // marks in its own controls. Naming the framework's three would answer for the framework
        // and silently refuse to update anybody else's control.
        if (members.FindContent(owner.GetType()) is { CanRead: true } content)
        {
            object? held = Read(owner, content.Name, members);

            if (held is not null && ReplaceInList(held, previous, fresh) is { } inContent)
            {
                return Report(element, diagnostics, inContent, $"{content.Name} would not take it");
            }

            if (ReferenceEquals(held, previous))
            {
                return Report(
                    element,
                    diagnostics,
                    Write(owner, content.Name, fresh, members, diagnostics, XamlObjectExposure.Live),
                    $"{content.Name} could not be written");
            }
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
    /// be applied has not half-applied itself. A collection that throws once the moves have begun
    /// is the exception, and is reported as one.
    /// </para>
    /// </remarks>
    /// <param name="objects">Which element each object came from.</param>
    /// <param name="parent">The element whose children changed places.</param>
    /// <param name="order">Its children as they were, in the order the new document gives them.</param>
    /// <param name="members">What decides which member holds the children.</param>
    /// <param name="diagnostics">Collects a report when the order cannot be applied.</param>
    /// <returns>Whether the children were reordered, refused, or left part-way.</returns>
    internal static XamlMutationOutcome Reorder(
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

        return Rearrange(slot, wanted) switch
        {
            XamlMutationOutcome.Applied => XamlMutationOutcome.Applied,
            XamlMutationOutcome.Refused => FailOrder(
                parent, diagnostics, $"{slot.GetType().Name} does not say how to move an item"),
            _ => BrokeOrder(
                parent, diagnostics, $"{slot.GetType().Name} threw part-way through the moves"),
        };
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

        if (objects.GetObject(parent) is not { } holder)
        {
            return null;
        }

        // The children live in the member marked [Content] where there is one — a panel's
        // Children, an items control's Items — and in the object itself where there is not.
        return members.FindContent(holder.GetType()) is { CanRead: true } content
            ? Read(holder, content.Name, members) ?? holder
            : holder;
    }

    /// <summary>Moves the items of a collection into a given order, without removing any.</summary>
    /// <remarks>
    /// Through the collection's own <c>Move</c>, which is what Avalonia's lists offer and what
    /// keeps a control attached to its parent throughout. Writing the positions instead would put
    /// one control in two places for as long as it took to write the second, which is how Avalonia
    /// is made to throw. A <c>Move</c> takes an item out and puts it back, so one that throws has
    /// already changed the collection whether or not it finished.
    /// </remarks>
    private static XamlMutationOutcome Rearrange(object slot, List<object> order)
    {
        if (slot is not IEnumerable items)
        {
            return XamlMutationOutcome.Refused;
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
            return XamlMutationOutcome.Refused;
        }

        MethodInfo? move = slot.GetType().GetMethod(
            "Move", BindingFlags.Public | BindingFlags.Instance, null, [typeof(int), typeof(int)], null);

        if (move is null)
        {
            return XamlMutationOutcome.Refused;
        }

        var positions = new int[order.Count];

        for (int target = 0; target < order.Count; target++)
        {
            positions[target] = current.FindIndex(item => ReferenceEquals(item, order[target]));

            if (positions[target] < 0)
            {
                return XamlMutationOutcome.Refused;
            }
        }

        // Resolved above, applied here: by this point nothing left can refuse, and anything that
        // throws anyway has already moved something.
        for (int target = 0; target < order.Count; target++)
        {
            int position = current.FindIndex(target, item => ReferenceEquals(item, order[target]));

            if (position == target)
            {
                continue;
            }

            try
            {
                move.Invoke(slot, [position, target]);
            }
            catch (Exception error) when (Ordinary(error))
            {
                return XamlMutationOutcome.Inconsistent;
            }

            object? item = current[position];

            current.RemoveAt(position);
            current.Insert(target, item);
        }

        return XamlMutationOutcome.Applied;
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
    /// <param name="members">What decides which member the type calls its content.</param>
    /// <param name="diagnostics">Collects a report when the content cannot be moved across.</param>
    /// <returns>Whether the content was rebuilt, refused, or left part-way.</returns>
    internal static XamlMutationOutcome ReplaceContent(
        object target,
        object fresh,
        XamlElement element,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        if (target.GetType() != fresh.GetType())
        {
            return Fail(element, diagnostics, "the rebuilt object is not the same kind of object");
        }

        // Whatever the type says its content is, moved across from the rebuilt copy. A dictionary
        // has no [Content] and is handled below, because what it holds is keys and merged files
        // rather than a member.
        if (target is not IResourceDictionary
            && members.FindContent(target.GetType()) is { CanRead: true } content)
        {
            return MoveContent(target, fresh, content, members, element, diagnostics);
        }

        return target is IResourceDictionary dictionary && fresh is IResourceDictionary rebuilt
            ? Refill(dictionary, rebuilt, element, diagnostics)
            : Fail(element, diagnostics, $"{target.GetType().Name} does not say what it holds");
    }

    /// <summary>Replaces everything a resource dictionary holds with what a rebuilt one holds.</summary>
    /// <remarks>
    /// Merged dictionaries are not entries. A file that only merges other files has all of its
    /// content there and none of it under a key, so copying the keys alone would leave the old
    /// content in place and call it an update.
    /// </remarks>
    private static XamlMutationOutcome Refill(
        IResourceDictionary dictionary,
        IResourceDictionary rebuilt,
        XamlElement element,
        List<MarkupDiagnostic> diagnostics)
    {
        int before = dictionary.Count;

        try
        {
            dictionary.Clear();
        }
        catch (Exception error) when (Ordinary(error))
        {
            return dictionary.Count == before
                ? Fail(element, diagnostics, $"the dictionary refused to be emptied: {error.Message}")
                : Broke(element, diagnostics, $"the dictionary threw while being emptied: {error.Message}");
        }

        // From here the dictionary has been emptied, so anything that goes wrong has already
        // changed it and no report can call the objects untouched.
        try
        {
            foreach (object key in rebuilt.Keys.ToArray())
            {
                dictionary[key] = rebuilt[key];
            }

            IResourceProvider[] merged = [.. rebuilt.MergedDictionaries];

            dictionary.MergedDictionaries.Clear();

            // Taken out of the copy first, for the same reason a control is: a provider that
            // two dictionaries both hold has an owner that is no longer true.
            rebuilt.MergedDictionaries.Clear();

            foreach (IResourceProvider provider in merged)
            {
                dictionary.MergedDictionaries.Add(provider);
            }

            return XamlMutationOutcome.Applied;
        }
        catch (Exception error) when (Ordinary(error))
        {
            return Broke(element, diagnostics, $"the emptied dictionary would not take it back: {error.Message}");
        }
    }

    /// <summary>Moves what a rebuilt copy holds in its content member onto the object that stays.</summary>
    /// <remarks>
    /// Taken out of the copy before it is put into the original, always. A control belongs to one
    /// parent, and adding it to a second while the first still holds it is how Avalonia is made to
    /// throw.
    /// </remarks>
    private static XamlMutationOutcome MoveContent(
        object target,
        object fresh,
        XamlMemberDescriptor content,
        XamlMemberResolver members,
        XamlElement element,
        List<MarkupDiagnostic> diagnostics)
    {
        object? held = Read(target, content.Name, members);
        object? rebuilt = Read(fresh, content.Name, members);

        if (held is IEnumerable && rebuilt is IEnumerable)
        {
            return MoveItems(held!, rebuilt!) switch
            {
                XamlMutationOutcome.Applied => XamlMutationOutcome.Applied,

                // A collection that refuses to be written through — an items control whose items
                // come from ItemsSource says exactly this — is an ordinary answer to an ordinary
                // edit, and the caller is told rather than shown an exception out of an update.
                XamlMutationOutcome.Refused => Fail(
                    element, diagnostics, $"{content.Name} would not take its content back"),
                _ => Broke(
                    element, diagnostics, $"{content.Name} was emptied and would not take its content back"),
            };
        }

        if (!content.CanWrite)
        {
            return Fail(
                element,
                diagnostics,
                $"{content.Name} is neither a list nor writable, so its content cannot be moved");
        }

        // Taken out of the copy first, and only where that matters: something in the logical
        // world belongs to one parent, and writing it onto the original while the copy still
        // holds it is how Avalonia is made to throw. A value that is nobody's child — a
        // string, a number — has no such rule, and a member that refuses null must not fail
        // the update over one. The copy is not live, so failing here changes nothing.
        if (rebuilt is ILogical
            && Write(fresh, content.Name, null, members, Quiet, XamlObjectExposure.Rebuilt)
                != XamlMutationOutcome.Applied)
        {
            return Fail(element, diagnostics, $"{content.Name} could not be cleared on the rebuilt copy");
        }

        return Report(
            element,
            diagnostics,
            Write(target, content.Name, rebuilt, members, diagnostics, XamlObjectExposure.Live),
            $"{content.Name} could not be written");
    }

    /// <summary>Diagnostics nobody reads, for a step whose failure is reported by its caller.</summary>
    private static List<MarkupDiagnostic> Quiet => [];

    /// <summary>Moves the items of one collection into another, whichever list interface it has.</summary>
    /// <remarks>
    /// Emptied before anything is added: an item that belongs to one parent cannot be handed to a
    /// second while the first still holds it. That ordering is also why this can fail in two
    /// different ways — a collection that refuses to be emptied has changed nothing, and one that
    /// refuses to be filled has already lost what it held.
    /// </remarks>
    private static XamlMutationOutcome MoveItems(object into, object from)
    {
        // Asked before anything is emptied. A collection that says it is read-only — an items
        // control reading ItemsSource says exactly that — is refused here, with nothing invoked
        // and nothing to be uncertain about afterwards. This is the check that keeps the common
        // case a clean refusal now that invoking and catching no longer can.
        if (into is IList { IsReadOnly: true } or IList { IsFixedSize: true })
        {
            return XamlMutationOutcome.Refused;
        }

        object?[] items;

        try
        {
            items = [.. ((IEnumerable)from).Cast<object?>()];
        }
        catch (Exception error) when (Ordinary(error))
        {
            return XamlMutationOutcome.Refused;
        }

        // The rebuilt copy, which nothing outside this update has ever seen. Emptying it changes
        // nothing a caller could observe, so a failure here is still a clean one.
        if (Clear(from, XamlObjectExposure.Rebuilt) != XamlMutationOutcome.Applied)
        {
            return XamlMutationOutcome.Refused;
        }

        XamlMutationOutcome emptied = Clear(into, XamlObjectExposure.Live);

        if (emptied != XamlMutationOutcome.Applied)
        {
            return emptied;
        }

        return items.All(item => Append(into, item))
            ? XamlMutationOutcome.Applied
            : XamlMutationOutcome.Inconsistent;
    }

    /// <summary>Empties a collection through whichever list interface it has.</summary>
    /// <remarks>
    /// A collection with no <c>Clear</c> at all is refused without anything being called. One that
    /// throws out of its own <c>Clear</c> has been running, and how far it got is not a question
    /// counting the items afterwards answers — a collection is free to remove three of five and
    /// then throw, and free to have told its owner about it.
    /// </remarks>
    private static XamlMutationOutcome Clear(object collection, XamlObjectExposure exposure)
    {
        try
        {
            if (collection is IList list)
            {
                list.Clear();

                return XamlMutationOutcome.Applied;
            }

            MethodInfo? clear = collection.GetType()
                .GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, []);

            if (clear is null)
            {
                return XamlMutationOutcome.Refused;
            }

            clear.Invoke(collection, []);

            return XamlMutationOutcome.Applied;
        }
        catch (Exception error) when (Ordinary(error))
        {
            return exposure == XamlObjectExposure.Live
                ? XamlMutationOutcome.Inconsistent
                : XamlMutationOutcome.Refused;
        }
    }

    /// <summary>Adds to a collection through whichever list interface it has.</summary>
    private static bool Append(object collection, object? item)
    {
        try
        {
            if (collection is IList list)
            {
                list.Add(item);

                return true;
            }

            MethodInfo? add = collection.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "Add" && method.GetParameters().Length == 1);

            if (add is null)
            {
                return false;
            }

            add.Invoke(collection, [item]);

            return true;
        }
        catch (Exception error) when (Ordinary(error))
        {
            return false;
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

    /// <summary>
    /// Replaces a dictionary entry by reference, or reports that this dictionary does not hold it.
    /// </summary>
    private static XamlMutationOutcome? ReplaceInDictionary(
        IResourceDictionary dictionary,
        object previous,
        object fresh)
    {
        foreach (object key in dictionary.Keys.ToArray())
        {
            if (!ReferenceEquals(dictionary[key], previous))
            {
                continue;
            }

            try
            {
                dictionary[key] = fresh;

                return XamlMutationOutcome.Applied;
            }
            catch (Exception error) when (Ordinary(error))
            {
                // The entry was found and the write was attempted, so whether the old value is
                // still under that key is not something this can claim either way.
                return XamlMutationOutcome.Inconsistent;
            }
        }

        return null;
    }

    /// <summary>
    /// Replaces an item of a collection, by reference, whichever list interface it offers, or
    /// reports that this collection does not hold it.
    /// </summary>
    /// <remarks>
    /// Avalonia's own collections — <c>Styles</c>, <c>Controls</c>, a dictionary's merged list —
    /// implement <see cref="IList{T}"/> and not the non-generic <see cref="IList"/>, so testing
    /// for the latter alone finds none of the ones this actually has to put things back into.
    /// </remarks>
    private static XamlMutationOutcome? ReplaceInList(object slot, object previous, object fresh)
    {
        if (slot is not IEnumerable items)
        {
            return null;
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

        return null;
    }

    /// <summary>Writes one position of a collection through its indexer.</summary>
    /// <remarks>
    /// A list assignment takes the old item out and puts the new one in, so one that throws has
    /// already changed the collection whether or not it finished.
    /// </remarks>
    private static XamlMutationOutcome Set(object slot, int index, object value)
    {
        // Asked rather than found out, for the same reason as everywhere else here.
        if (slot is IList { IsReadOnly: true })
        {
            return XamlMutationOutcome.Refused;
        }

        try
        {
            if (slot is IList list)
            {
                list[index] = value;

                return XamlMutationOutcome.Applied;
            }

            PropertyInfo? indexer = slot.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(property =>
                    property.CanWrite && property.GetIndexParameters() is [{ ParameterType.Name: nameof(Int32) }]);

            if (indexer is null)
            {
                return XamlMutationOutcome.Refused;
            }

            indexer.SetValue(slot, value, [index]);

            return XamlMutationOutcome.Applied;
        }
        catch (Exception error) when (Ordinary(error))
        {
            return XamlMutationOutcome.Inconsistent;
        }
    }

    /// <summary>Reads a member, treating a getter that throws as an answer of nothing.</summary>
    /// <remarks>
    /// Reading calls a control library's own code, which may throw for reasons that have nothing
    /// to do with this update. It changes nothing either way, so the caller carries on to whatever
    /// it would have done had the member not been there.
    /// </remarks>
    private static object? Read(object owner, string memberName, XamlMemberResolver members)
    {
        XamlMemberDescriptor member = members.Resolve(owner.GetType(), memberName);

        try
        {
            return member switch
            {
                { AvaloniaProperty: { } property } when owner is Avalonia.AvaloniaObject avaloniaObject =>
                    avaloniaObject.GetValue(property),
                { ClrProperty.CanRead: true } => member.ClrProperty!.GetValue(owner),
                _ => null,
            };
        }
        catch (Exception error) when (Ordinary(error))
        {
            return null;
        }
    }

    /// <summary>Writes a member, saying what running its setter may have cost.</summary>
    /// <remarks>
    /// The member is asked whether it can be written before it is written, and one that says no is
    /// refused with nothing invoked. Past that point the setter runs, and a setter that throws has
    /// already had its chance to do whatever it liked first — so on a live object the honest answer
    /// is that its state is unknown, and only on a copy nobody has seen is it still a refusal.
    /// </remarks>
    private static XamlMutationOutcome Write(
        object owner,
        string memberName,
        object? value,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics,
        XamlObjectExposure exposure)
    {
        XamlMemberDescriptor member = members.Resolve(owner.GetType(), memberName);

        if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
        {
            return XamlMutationOutcome.Refused;
        }

        try
        {
            XamlDesignValues.Write(owner, member, value);

            return XamlMutationOutcome.Applied;
        }
        catch (Exception error) when (Ordinary(error))
        {
            // A CLR property and an attached accessor pair are written by reflection, which wraps
            // whatever the setter threw; the message a caller reads should be the setter's.
            Exception refusal = (error as TargetInvocationException)?.InnerException ?? error;

            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.IncompatibleValue,
                $"{owner.GetType().Name}.{memberName} threw while being written: {refusal.Message}",
                MarkupDiagnosticSeverity.Error));

            return exposure == XamlObjectExposure.Live
                ? XamlMutationOutcome.Inconsistent
                : XamlMutationOutcome.Refused;
        }
    }

    /// <summary>
    /// The failures that belong to the document rather than to a broken invariant.
    /// </summary>
    /// <remarks>
    /// A setter refusing a value, a collection refusing to be written through, a getter that
    /// throws: an update is a thing a user does to a document, and none of these is a reason to
    /// throw out of one. Anything else — a null reference inside a control, an out-of-memory —
    /// is not an answer about the document and is left to propagate.
    /// </remarks>
    private static bool Ordinary(Exception error) =>
        error is InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or InvalidCastException
            or TargetInvocationException;

    /// <summary>Turns a step's outcome into the reported one, describing a refusal.</summary>
    private static XamlMutationOutcome Report(
        XamlElement element,
        List<MarkupDiagnostic> diagnostics,
        XamlMutationOutcome outcome,
        string reason) =>
        outcome switch
        {
            XamlMutationOutcome.Applied => XamlMutationOutcome.Applied,
            XamlMutationOutcome.Refused => Fail(element, diagnostics, reason),
            _ => Broke(element, diagnostics, reason),
        };

    private static XamlMutationOutcome FailOrder(
        XamlElement element,
        List<MarkupDiagnostic> diagnostics,
        string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.UpdateNotApplied,
            $"The children of <{element.Name}> could not be put in the order the document gives them: {reason}.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return XamlMutationOutcome.Refused;
    }

    private static XamlMutationOutcome BrokeOrder(
        XamlElement element,
        List<MarkupDiagnostic> diagnostics,
        string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
            $"The children of <{element.Name}> were part-way through being reordered when {reason}. " +
            "Some have moved and some have not.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return XamlMutationOutcome.Inconsistent;
    }

    private static XamlMutationOutcome Fail(
        XamlElement element,
        List<MarkupDiagnostic> diagnostics,
        string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.UpdateNotApplied,
            $"<{element.Name}> was rebuilt but could not be put back: {reason}.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return XamlMutationOutcome.Refused;
    }

    private static XamlMutationOutcome Broke(
        XamlElement element,
        List<MarkupDiagnostic> diagnostics,
        string reason)
    {
        diagnostics.Add(MarkupDiagnostic.Synchronization(
            XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
            $"<{element.Name}> was part-way through being replaced when {reason}. " +
            "What it holds now describes neither document.",
            MarkupDiagnosticSeverity.Error,
            element.Document.Uri,
            element.NameSpan));

        return XamlMutationOutcome.Inconsistent;
    }
}
