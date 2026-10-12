using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What checking a binding written on an element found: its source, its path and whether a build takes it.</summary>
/// <remarks>
/// An answer to one question at one moment. <see cref="SourceType"/> and <see cref="ResultType"/> are types of
/// whatever generation of code the session was built in, and a tool that keeps them keeps that generation: read
/// what it needs from them and let them go.
/// </remarks>
public sealed class XamlBindingCheck
{
    /// <summary>Gets what the binding reads from.</summary>
    public required XamlBindingSource Source { get; init; }

    /// <summary>Gets what reading the path against the source found.</summary>
    public required XamlBindingPathStatus Status { get; init; }

    /// <summary>
    /// Gets whether a build compiles the binding: <c>{CompiledBinding}</c> always, <c>{ReflectionBinding}</c>
    /// never, and <c>{Binding}</c> as the nearest <c>x:CompileBindings</c> says, or the build's default where none
    /// is written.
    /// </summary>
    public required bool Compiles { get; init; }

    /// <summary>Gets the path read against the source, without what names the source: <c>Text</c> of <c>#title.Text</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the type the path was read against, when the source has one this reading knows.</summary>
    public Type? SourceType { get; init; }

    /// <summary>Gets the type the path ends at, when it resolved.</summary>
    public Type? ResultType { get; init; }

    /// <summary>
    /// Gets the step the reading stopped at — the one that names nothing, or the first one it does not follow;
    /// <see langword="null"/> for a path that resolved, and for a source that is missing rather than a step.
    /// </summary>
    public string? Step { get; init; }

    /// <summary>Gets what a tool can show beside a binding that did not resolve.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets whether a build of the document would fail on the binding: it compiles, and what it reads is missing.
    /// </summary>
    public bool BreaksBuild => Compiles && Status == XamlBindingPathStatus.Broken;
}
