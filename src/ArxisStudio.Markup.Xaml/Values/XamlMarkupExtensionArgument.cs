using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// One argument of a markup extension: a value, and the name it was given if it had one.
/// </summary>
/// <param name="Name">The argument's name, or <see langword="null"/> when it is positional.</param>
/// <param name="Value">The argument's value, which may itself be a nested extension.</param>
public sealed record XamlMarkupExtensionArgument(string? Name, XamlValue Value)
{
    /// <summary>Gets the argument's value.</summary>
    public XamlValue Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <summary>Gets a value indicating whether the argument was given by position rather than name.</summary>
    public bool IsPositional => Name is null;

    /// <summary>Returns the argument as it would be written.</summary>
    /// <returns>The argument text.</returns>
    public override string ToString() =>
        Name is null ? Value.ToXamlText() : $"{Name}={Value.ToXamlText()}";
}
