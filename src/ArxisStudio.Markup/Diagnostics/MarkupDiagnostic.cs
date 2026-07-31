using System;
using System.Collections.Immutable;

namespace ArxisStudio.Markup;

/// <summary>
/// A structured report of something the library noticed, in place of an exception.
/// </summary>
/// <remarks>
/// <para>
/// Ordinary user errors — a malformed tag, an unresolved type, a missing resource — are
/// reported here rather than thrown. A caller decides what a diagnostic means for it by
/// looking at <see cref="Code"/>, <see cref="Category"/> and <see cref="Severity"/>, never by
/// inspecting <see cref="Message"/>: the message is for people and may be reworded, while the
/// code is a stable contract.
/// </para>
/// <para>
/// Exceptions remain reserved for invalid use of the library's own API, disposed sessions,
/// broken internal invariants, cancellation, and failures with no structured result to return.
/// </para>
/// </remarks>
/// <param name="Code">
/// A stable, machine-readable identifier. Each package owns a prefix; codes are never reused
/// for a different meaning, because consumers suppress and route on them.
/// </param>
/// <param name="Message">A human-readable explanation. Not a stable contract.</param>
/// <param name="Severity">How seriously to take it.</param>
/// <param name="DocumentUri">The document concerned, when there is one.</param>
/// <param name="Span">The range concerned, when the diagnostic is more precise than the whole document.</param>
public sealed record MarkupDiagnostic(
    string Code,
    string Message,
    MarkupDiagnosticSeverity Severity,
    Uri? DocumentUri = null,
    TextSpan? Span = null)
{
    /// <summary>Gets the stable, machine-readable identifier.</summary>
    public string Code { get; init; } = string.IsNullOrWhiteSpace(Code)
        ? throw new ArgumentException("A diagnostic code cannot be null or blank.", nameof(Code))
        : Code;

    /// <summary>Gets the human-readable explanation.</summary>
    public string Message { get; init; } = Message ?? throw new ArgumentNullException(nameof(Message));

    /// <summary>
    /// Gets the pipeline stage this diagnostic came from. Defaults to
    /// <see cref="MarkupDiagnosticCategory.Parse"/>, the only stage that exists until the
    /// loader is implemented.
    /// </summary>
    public MarkupDiagnosticCategory Category { get; init; } = MarkupDiagnosticCategory.Parse;

    /// <summary>
    /// Gets secondary locations that make the diagnostic understandable, such as the other
    /// end of a cycle or the declaration a duplicate collides with.
    /// </summary>
    public ImmutableArray<MarkupLocation> RelatedLocations { get; init; } = [];

    /// <summary>Gets a value indicating whether this diagnostic prevents a correct result.</summary>
    public bool IsError => Severity == MarkupDiagnosticSeverity.Error;

    /// <summary>Gets this diagnostic's own location, when it has one.</summary>
    public MarkupLocation? Location =>
        DocumentUri is null ? null : new MarkupLocation(DocumentUri, Span);

    /// <summary>Creates a diagnostic about reading a document's text.</summary>
    /// <param name="code">The stable identifier.</param>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="severity">How seriously to take it.</param>
    /// <param name="documentUri">The document concerned.</param>
    /// <param name="span">The range concerned.</param>
    /// <returns>The diagnostic.</returns>
    public static MarkupDiagnostic Parse(
        string code,
        string message,
        MarkupDiagnosticSeverity severity = MarkupDiagnosticSeverity.Error,
        Uri? documentUri = null,
        TextSpan? span = null) =>
        new(code, message, severity, documentUri, span) { Category = MarkupDiagnosticCategory.Parse };

    /// <summary>Creates a diagnostic about resolving a name, type, member or resource.</summary>
    /// <param name="code">The stable identifier.</param>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="severity">How seriously to take it.</param>
    /// <param name="documentUri">The document concerned.</param>
    /// <param name="span">The range concerned.</param>
    /// <returns>The diagnostic.</returns>
    public static MarkupDiagnostic Resolution(
        string code,
        string message,
        MarkupDiagnosticSeverity severity = MarkupDiagnosticSeverity.Error,
        Uri? documentUri = null,
        TextSpan? span = null) =>
        new(code, message, severity, documentUri, span) { Category = MarkupDiagnosticCategory.Resolution };

    /// <summary>Creates a diagnostic about creating or initialising objects.</summary>
    /// <param name="code">The stable identifier.</param>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="severity">How seriously to take it.</param>
    /// <param name="documentUri">The document concerned.</param>
    /// <param name="span">The range concerned.</param>
    /// <returns>The diagnostic.</returns>
    public static MarkupDiagnostic Load(
        string code,
        string message,
        MarkupDiagnosticSeverity severity = MarkupDiagnosticSeverity.Error,
        Uri? documentUri = null,
        TextSpan? span = null) =>
        new(code, message, severity, documentUri, span) { Category = MarkupDiagnosticCategory.Load };

    /// <summary>Creates a diagnostic about keeping a document and its objects in step.</summary>
    /// <param name="code">The stable identifier.</param>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="severity">How seriously to take it.</param>
    /// <param name="documentUri">The document concerned.</param>
    /// <param name="span">The range concerned.</param>
    /// <returns>The diagnostic.</returns>
    public static MarkupDiagnostic Synchronization(
        string code,
        string message,
        MarkupDiagnosticSeverity severity = MarkupDiagnosticSeverity.Error,
        Uri? documentUri = null,
        TextSpan? span = null) =>
        new(code, message, severity, documentUri, span) { Category = MarkupDiagnosticCategory.Synchronization };

    /// <summary>Returns a copy carrying the given related locations.</summary>
    /// <param name="locations">The secondary locations.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locations"/> is <see langword="null"/>.</exception>
    public MarkupDiagnostic WithRelatedLocations(params MarkupLocation[] locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        return this with { RelatedLocations = [.. locations] };
    }

    /// <summary>Returns a string of the form <c>severity CODE: message</c> with the location, if any.</summary>
    /// <returns>A readable representation of the diagnostic.</returns>
    public override string ToString()
    {
        string location = Location is { } value ? $" ({value})" : string.Empty;

        return $"{Severity} {Code}: {Message}{location}";
    }
}
