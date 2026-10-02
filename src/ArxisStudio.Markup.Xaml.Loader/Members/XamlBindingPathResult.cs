using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What reading a binding path against a source type found.</summary>
/// <remarks>
/// An answer to one question at one moment. <see cref="ResultType"/> is a type of whatever
/// generation of code the source type came from, and a tool that keeps it keeps that generation:
/// read what it needs from it and let it go.
/// </remarks>
public sealed class XamlBindingPathResult
{
    /// <summary>Gets what the reading found.</summary>
    public required XamlBindingPathStatus Status { get; init; }

    /// <summary>Gets the type the path ends at, when it resolved.</summary>
    public Type? ResultType { get; init; }

    /// <summary>
    /// Gets the step the reading stopped at: the one that names nothing, or the first one it does not
    /// follow. <see langword="null"/> for a path that resolved.
    /// </summary>
    public string? Step { get; init; }

    /// <summary>Gets what a tool can show beside a binding that did not resolve.</summary>
    public string? Message { get; init; }
}
