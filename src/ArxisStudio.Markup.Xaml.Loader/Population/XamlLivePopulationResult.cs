using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What registering a live document for a type produced.
/// </summary>
public sealed class XamlLivePopulationResult
{
    /// <summary>
    /// Gets a value indicating whether instances of the type now populate from the document.
    /// </summary>
    /// <remarks>
    /// A fact rather than a verdict: <see langword="true"/> means the override is in place, even
    /// when the document carries diagnostics that will surface again at population time.
    /// </remarks>
    public required bool Installed { get; init; }

    /// <summary>Gets everything noticed while preparing the document.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }

    /// <summary>Gets a value indicating whether the registration succeeded without an error.</summary>
    public bool Success => Installed && !Diagnostics.Any(static diagnostic => diagnostic.IsError);
}
