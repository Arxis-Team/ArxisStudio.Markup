using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>An event attribute naming a handler the root's class declares.</summary>
/// <param name="Element">The element the event is written on.</param>
/// <param name="Attribute">The attribute that names the handler.</param>
/// <param name="Method">The name of the handler.</param>
internal sealed record XamlHandlerAttribute(XamlElement Element, XamlAttribute Attribute, string Method);

/// <summary>What the attribute checks found in one version of a document.</summary>
/// <param name="Withheld">The attributes a projection has to leave out for the load to survive.</param>
/// <param name="Handlers">The handlers the class answers, which a load hands to Avalonia.</param>
internal readonly record struct XamlAttributeFindings(
    ImmutableArray<TextSpan> Withheld,
    ImmutableArray<XamlHandlerAttribute> Handlers);

/// <summary>
/// Hooks the handlers of a rebuilt part up to the instance its document populated.
/// </summary>
/// <remarks>
/// <para>
/// A load gives Avalonia the instance, and Avalonia hooks each handler up to it. A part an update
/// rebuilds is loaded on its own, with no instance to hand over — Avalonia refuses a handler it
/// cannot hook up to anything, and a part holding one could not be rebuilt at all. So the part is
/// rebuilt without its handlers, and they are hooked up here, to the session's root, once the part's
/// objects exist.
/// </para>
/// <para>
/// Through public reflection alone: the event's own accessor and a delegate made from the method
/// the attribute names. A method of that name that cannot take the event's arguments is reported
/// and left unhooked — the rest of the update stands, because a handler the author is still writing
/// is the ordinary state of a file in an editor.
/// </para>
/// </remarks>
internal static class XamlHandlers
{
    /// <summary>Hooks one handler up to the object its element produced.</summary>
    /// <param name="target">The object the event is raised by.</param>
    /// <param name="handler">The attribute naming the handler.</param>
    /// <param name="root">The instance whose method handles it.</param>
    /// <param name="members">What decides which member the attribute names.</param>
    /// <param name="diagnostics">Collects a handler that could not be hooked up.</param>
    /// <param name="documentUri">The document the attribute is written in.</param>
    internal static void Hook(
        object target,
        XamlHandlerAttribute handler,
        object root,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics,
        Uri? documentUri)
    {
        if (members.Resolve(target.GetType(), handler.Attribute.Name.LocalName).Event
                is not { EventHandlerType: { } delegateType } raised
            || Bind(root, handler.Method, delegateType) is not { } callback)
        {
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.HandlerSignatureMismatch,
                $"'{handler.Attribute.Name}' names {root.GetType().Name}.{handler.Method}, which cannot handle " +
                $"{target.GetType().Name}.{handler.Attribute.Name.LocalName}, so it was not hooked up. " +
                "The rest of the update was applied.",
                MarkupDiagnosticSeverity.Warning,
                documentUri,
                handler.Attribute.Span));

            return;
        }

        raised.AddEventHandler(target, callback);
    }

    /// <summary>Makes a delegate of the event's type from the first method of that name that fits it.</summary>
    /// <remarks>
    /// The same methods the attribute checks find a handler among, so a handler is either found by
    /// both or by neither. Fitting is the delegate's own rule — parameters may be less derived than
    /// the event's — which is the rule Avalonia applies when it hooks a handler up itself.
    /// </remarks>
    private static Delegate? Bind(object root, string method, Type delegateType)
    {
        foreach (MethodInfo candidate in root.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.FlattenHierarchy))
        {
            if (!string.Equals(candidate.Name, method, StringComparison.Ordinal) || candidate.IsGenericMethodDefinition)
            {
                continue;
            }

            Delegate? bound = candidate.IsStatic
                ? Delegate.CreateDelegate(delegateType, candidate, throwOnBindFailure: false)
                : Delegate.CreateDelegate(delegateType, root, candidate, throwOnBindFailure: false);

            if (bound is not null)
            {
                return bound;
            }
        }

        return null;
    }
}
