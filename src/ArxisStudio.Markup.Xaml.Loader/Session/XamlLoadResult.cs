using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What loading a document produced.</summary>
/// <remarks>
/// A result rather than an exception. A document that half-loads is useful — a designer can
/// still show what was created and say what went wrong — and throwing would discard both.
/// </remarks>
public sealed class XamlLoadResult
{
    /// <summary>Gets the object the document produced, or <see langword="null"/> when it produced none.</summary>
    public object? RootObject { get; init; }

    /// <summary>Gets everything noticed while loading, in the order it was noticed.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }

    /// <summary>Gets a value indicating whether an object was produced without errors.</summary>
    public bool Success =>
        RootObject is not null && !Diagnostics.Any(static diagnostic => diagnostic.IsError);
}
