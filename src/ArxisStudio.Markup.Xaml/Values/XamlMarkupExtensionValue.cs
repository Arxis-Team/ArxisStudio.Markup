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

    /// <summary>
    /// Where each argument stands in <see cref="RawText"/>, from the first character of its name or value
    /// to the last of its value; empty when the value was constructed.
    /// </summary>
    internal ImmutableArray<TextSpan> ArgumentSpans { get; init; } = [];

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
        RawText ?? $"{{{TypeName}{RenderArguments(Arguments)}}}";

    /// <summary>
    /// Sets one argument and keeps everything else as it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the touched argument's text changes. The others — their spacing, their quotes, an argument
    /// this package does not understand, a nested extension — stay exactly as the document had them, so
    /// a tool that changes a binding's mode does not rewrite its converter. A value that was constructed
    /// rather than parsed has no text to keep and is written anew.
    /// </para>
    /// <para>
    /// A positional argument goes before the named ones, which is the only place a markup extension
    /// takes it; a new named argument goes last. Removing an argument takes its separator with it.
    /// </para>
    /// </remarks>
    /// <param name="name">The argument's name; <see langword="null"/> for the first positional argument.</param>
    /// <param name="value">The new value; <see langword="null"/> removes the argument.</param>
    /// <returns>The expression with the argument set, parsed from its new text; this one when there was nothing to remove.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or <paramref name="value"/> is a literal holding both kinds of quote.
    /// </exception>
    public XamlMarkupExtensionValue WithArgument(string? name, XamlValue? value)
    {
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
        }

        int index = IndexOf(name);

        if (index < 0 && value is null)
        {
            return this;
        }

        string? argument = value is null ? null : XamlMarkupExtensionArgument.Write(name, value);
        string text = RawText is { } raw && KeepsItsText(raw)
            ? Spliced(raw, index, name is null, argument)
            : Rendered(index, name, value);

        return (XamlMarkupExtensionValue)XamlMarkupExtensionParser.ParseValue(text, out _);
    }

    /// <summary>Where the argument is: the first positional one, or the one with this name; -1 — none.</summary>
    private int IndexOf(string? name)
    {
        for (int index = 0; index < Arguments.Length; index++)
        {
            if (name is null ? Arguments[index].IsPositional : string.Equals(Arguments[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether the argument spans still describe the text: a value whose text was set by hand is written anew.</summary>
    private bool KeepsItsText(string raw) =>
        ArgumentSpans.Length == Arguments.Length
        && raw.StartsWith('{')
        && ArgumentSpans.All(span => span.End <= raw.Length);

    /// <summary>The written text with one argument replaced, removed or added and the rest left as they were.</summary>
    private string Spliced(string raw, int index, bool positional, string? argument)
    {
        if (index >= 0)
        {
            TextSpan span = ArgumentSpans[index];

            if (argument is not null)
            {
                return raw[..span.Start] + argument + raw[span.End..];
            }

            // A removed argument takes the separator after it, or before it when it was the last.
            if (index + 1 < ArgumentSpans.Length)
            {
                return raw[..span.Start] + raw[ArgumentSpans[index + 1].Start..];
            }

            return index > 0
                ? raw[..ArgumentSpans[index - 1].End] + raw[span.End..]
                : raw[..span.Start].TrimEnd() + raw[span.End..];
        }

        if (positional && ArgumentSpans.Length > 0)
        {
            int first = ArgumentSpans[0].Start;

            return raw[..first] + argument + ", " + raw[first..];
        }

        if (ArgumentSpans.Length > 0)
        {
            int last = ArgumentSpans[^1].End;

            return raw[..last] + ", " + argument + raw[last..];
        }

        // No arguments yet: the argument goes after the name, before the closing brace if there is one.
        int close = raw.EndsWith('}') ? raw.Length - 1 : raw.Length;

        return raw[..close].TrimEnd() + " " + argument + raw[close..];
    }

    /// <summary>The expression written anew from its arguments, with one replaced, removed or added.</summary>
    private string Rendered(int index, string? name, XamlValue? value)
    {
        List<XamlMarkupExtensionArgument> arguments = [.. Arguments];
        XamlMarkupExtensionArgument? set = value is null ? null : new XamlMarkupExtensionArgument(name, value);

        if (index >= 0)
        {
            if (set is null)
            {
                arguments.RemoveAt(index);
            }
            else
            {
                arguments[index] = set;
            }
        }
        else if (set is not null)
        {
            arguments.Insert(name is null ? 0 : arguments.Count, set);
        }

        return $"{{{TypeName}{RenderArguments(arguments)}}}";
    }

    private static string RenderArguments(IReadOnlyCollection<XamlMarkupExtensionArgument> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : " " + string.Join(", ", arguments.Select(static argument => argument.ToString()));
}
