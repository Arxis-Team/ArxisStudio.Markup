using System;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Says that an instance could not be populated from its live document, and was populated from
/// the compiled markup instead.
/// </summary>
/// <remarks>
/// An event rather than a result, because population happens inside a constructor somebody else
/// is running — there is no call of this library's on the stack to return anything from. The
/// instance is still whole: the compiled markup is what it would have shown had nothing been
/// registered, so the failure costs freshness, never a blank control.
/// </remarks>
public sealed class XamlLivePopulationFailedEventArgs : EventArgs
{
    internal XamlLivePopulationFailedEventArgs(
        Type controlType, XamlDocument document, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        ControlType = controlType;
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the type whose instance fell back to its compiled markup.</summary>
    public Type ControlType { get; }

    /// <summary>Gets the live document that could not populate it.</summary>
    public XamlDocument Document { get; }

    /// <summary>Gets what went wrong.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }
}
