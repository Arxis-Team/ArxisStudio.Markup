using System;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// What an attribute or member was set to, in the form the document expresses it.
/// </summary>
/// <remarks>
/// <para>
/// The distinction this type draws is the one the whole project exists to protect. A binding
/// is not the value it currently produces, and a static-resource reference is not the brush it
/// currently resolves to. Collapsing either into a converted CLR value would let a save
/// silently replace <c>{Binding Customer.Name}</c> with whatever the text box happened to be
/// showing.
/// </para>
/// <para>
/// Nothing here is executed or resolved. A markup extension is parsed into its parts and left
/// alone; what <c>StaticResource</c> means is a question for the loader.
/// </para>
/// </remarks>
public abstract record XamlValue
{
    /// <summary>Gets the value meaning "no value was written".</summary>
    public static XamlValue Unset { get; } = new XamlUnsetValue();

    /// <summary>
    /// Reads the text of an attribute value into the form it expresses.
    /// </summary>
    /// <remarks>
    /// Text beginning with <c>{</c> is a markup extension, except for the <c>{}</c> escape,
    /// which XAML defines precisely so a literal can start with a brace.
    /// </remarks>
    /// <param name="text">The raw attribute text, with entity references left unexpanded.</param>
    /// <returns>The parsed value. Malformed extensions still produce a value, with diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlValue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XamlMarkupExtensionParser.ParseValue(text, out _);
    }

    /// <summary>Reads the text of an attribute value, reporting anything malformed.</summary>
    /// <param name="text">The raw attribute text.</param>
    /// <param name="diagnostics">Diagnostics raised while parsing, empty when the text is well-formed.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlValue Parse(string text, out ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XamlMarkupExtensionParser.ParseValue(text, out diagnostics);
    }

    /// <summary>
    /// Renders the value as it would appear between an attribute's quotes.
    /// </summary>
    /// <remarks>
    /// A value read from a document renders back to exactly the text it was read from, spacing
    /// included. Only a value the caller constructed is rendered from its parts.
    /// </remarks>
    /// <returns>The attribute text.</returns>
    public abstract string ToXamlText();

    /// <summary>Returns the value as it would be written.</summary>
    /// <returns>The attribute text.</returns>
    public override string ToString() => ToXamlText();
}
