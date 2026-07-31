using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What an attempt to bring a session's objects in line with a changed document did.
/// </summary>
/// <remarks>
/// An update either lands whole or does not land at all. A half-applied one would leave the
/// objects agreeing with neither the document they came from nor the one they were given, and no
/// caller could tell which parts were which.
/// </remarks>
public sealed class XamlUpdateResult
{
    /// <summary>Gets a value indicating whether the objects and the session's document moved on.</summary>
    public required bool Applied { get; init; }

    /// <summary>Gets the largest strategy the update called for.</summary>
    public required XamlUpdateStrategy Strategy { get; init; }

    /// <summary>Gets the changes the comparison found, in document order.</summary>
    public required ImmutableArray<XamlDocumentChange> Changes { get; init; }

    /// <summary>Gets everything noticed on the way.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }
}
