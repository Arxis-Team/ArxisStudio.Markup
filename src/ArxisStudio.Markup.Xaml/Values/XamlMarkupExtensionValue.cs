using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A <c>{Extension arguments}</c> expression, parsed into its parts and left unexecuted.
/// </summary>
/// <remarks>
/// <para>
/// The expression's original text is kept alongside the parsed parts. Rendering a value that
/// came from a document returns that text verbatim, so an edit elsewhere in the same attribute
/// cannot quietly reflow a binding's spacing or reorder its arguments — a diff that showed
/// such a change would be a diff nobody asked for.
/// </para>
/// <para>
/// Nothing here resolves the extension's type or runs it. Whether <c>StaticResource</c> finds
/// anything is a question for the loader.
/// </para>
/// </remarks>
/// <param name="TypeName">The extension's type name as written, prefix included.</param>
/// <param name="Arguments">The arguments in source order, positional and named alike.</param>
public sealed record XamlMarkupExtensionValue(
    XamlQualifiedName TypeName,
    ImmutableArray<XamlMarkupExtensionArgument> Arguments) : XamlValue
{
    /// <summary>Gets the arguments in source order.</summary>
    public ImmutableArray<XamlMarkupExtensionArgument> Arguments { get; } =
        Arguments.IsDefault ? [] : Arguments;

    /// <summary>
    /// Gets the expression exactly as it was written, or <see langword="null"/> when this value
    /// was constructed rather than parsed.
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>Gets the arguments that were given by position, in order.</summary>
    public IEnumerable<XamlMarkupExtensionArgument> PositionalArguments =>
        Arguments.Where(static argument => argument.IsPositional);

    /// <summary>Gets the arguments that were given by name, in source order.</summary>
    public IEnumerable<XamlMarkupExtensionArgument> NamedArguments =>
        Arguments.Where(static argument => !argument.IsPositional);

    /// <summary>Finds a named argument.</summary>
    /// <param name="name">The argument name, compared ordinally.</param>
    /// <returns>The argument, or <see langword="null"/> when the extension has no such argument.</returns>
    public XamlMarkupExtensionArgument? GetArgument(string name) =>
        Arguments.FirstOrDefault(argument => string.Equals(argument.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToXamlText() =>
        RawText ?? $"{{{TypeName}{RenderArguments()}}}";

    private string RenderArguments() =>
        Arguments.IsEmpty
            ? string.Empty
            : " " + string.Join(", ", Arguments.Select(static argument => argument.ToString()));
}
