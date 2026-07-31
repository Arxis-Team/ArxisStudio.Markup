using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>Text, meaning exactly what it says.</summary>
/// <remarks>
/// <para>
/// <see cref="Text"/> is raw attribute text — the same thing
/// <see cref="XamlAttribute.GetValueText"/> returns, with entity references left unexpanded.
/// Keeping it raw is what makes reading a value and writing it back a no-op; expanding on read
/// and re-escaping on write would grow <c>&amp;amp;</c> into <c>&amp;amp;amp;</c> a little more
/// with every save. Callers holding text that has never been escaped should build the value
/// with <see cref="FromPlainText"/>.
/// </para>
/// <para>
/// A literal that begins with <c>{</c> would otherwise read as a markup extension, so it is
/// written with XAML's <c>{}</c> escape. That escape is a property of how the value is written,
/// not part of the value, so <see cref="Text"/> never contains it.
/// </para>
/// </remarks>
/// <param name="Text">The raw attribute text, without any <c>{}</c> escape prefix.</param>
public sealed record XamlLiteralValue(string Text) : XamlValue
{
    /// <summary>Gets the raw attribute text.</summary>
    public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));

    /// <summary>
    /// Creates a literal from text that has never been escaped, escaping what XML reserves.
    /// </summary>
    /// <param name="text">Text meant literally, such as a value typed by a user.</param>
    /// <returns>The literal, with <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> escaped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlLiteralValue FromPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new XamlLiteralValue(text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public override string ToXamlText() =>
        Text.StartsWith('{') ? "{}" + Text : Text;
}
