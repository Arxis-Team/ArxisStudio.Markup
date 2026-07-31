using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What resolving a type name found, or why it found nothing.
/// </summary>
/// <remarks>
/// A result rather than an exception, because a document naming a type that is not available
/// is ordinary — the assembly may simply not have been supplied yet — and a caller usually
/// wants to carry on with the rest of the document.
/// </remarks>
public sealed record XamlTypeResolution
{
    private XamlTypeResolution(Type? type, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Type = type;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the resolved type, or <see langword="null"/> when resolution failed.</summary>
    public Type? Type { get; }

    /// <summary>Gets the diagnostics explaining a failure, empty on success.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

    /// <summary>Gets a value indicating whether a type was found.</summary>
    [MemberNotNullWhen(true, nameof(Type))]
    public bool Success => Type is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="type">The resolved type.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public static XamlTypeResolution Resolved(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new XamlTypeResolution(type, []);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="diagnostics">The diagnostics explaining the failure.</param>
    /// <returns>The result.</returns>
    public static XamlTypeResolution Failed(params MarkupDiagnostic[] diagnostics) =>
        new(null, [.. diagnostics ?? []]);

    /// <summary>Returns the resolved type or the first diagnostic.</summary>
    /// <returns>A readable description of the result.</returns>
    public override string ToString() =>
        Type?.FullName ?? Diagnostics.FirstOrDefault()?.Message ?? "unresolved";
}
