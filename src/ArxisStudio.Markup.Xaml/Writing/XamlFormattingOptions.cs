using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// How <see cref="XamlWriteMode.Format"/> should lay a document out.
/// </summary>
/// <remarks>
/// Every choice is explicit and has a stated default. Nothing is inferred from the document
/// being formatted, because a formatter that guessed would produce different output for the
/// same options depending on what it was given.
/// </remarks>
public sealed class XamlFormattingOptions
{
    /// <summary>Gets the options used when none are supplied: four spaces and the platform's line ending.</summary>
    public static XamlFormattingOptions Default { get; } = new();

    /// <summary>Gets the string used for one level of indentation.</summary>
    public string Indentation { get; init; } = "    ";

    /// <summary>Gets the line ending written between lines.</summary>
    public string NewLine { get; init; } = Environment.NewLine;

    /// <summary>Gets the character attribute values are quoted with.</summary>
    public char AttributeQuote { get; init; } = '"';

    /// <summary>Gets a value indicating whether each attribute is written on its own line.</summary>
    public bool PutAttributesOnSeparateLines { get; init; }

    /// <summary>
    /// Gets a value indicating whether comments are kept.
    /// </summary>
    /// <remarks>
    /// Defaults to keeping them, and there is no good reason to turn it off. A formatter that
    /// dropped comments would be destroying something the author wrote deliberately.
    /// </remarks>
    public bool PreserveComments { get; init; } = true;
}
