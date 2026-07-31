using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a controlled edit did, and what it noticed.</summary>
/// <remarks>
/// A result rather than an exception, because most reasons an edit cannot proceed — a read-only
/// property, a member the type does not have — are things a caller wants to show rather than
/// crash on.
/// </remarks>
public sealed class XamlEditResult
{
    /// <summary>Gets a value indicating whether the edit was applied.</summary>
    public required bool Applied { get; init; }

    /// <summary>Gets everything noticed while applying it.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }

    /// <summary>Gets a value indicating whether anything was reported as an error.</summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.IsError);

    /// <summary>Returns whether the edit applied, with the first diagnostic if there is one.</summary>
    /// <returns>A readable description of the result.</returns>
    public override string ToString() =>
        Applied
            ? $"applied ({Diagnostics.Length} diagnostics)"
            : Diagnostics.FirstOrDefault()?.Message ?? "not applied";
}
