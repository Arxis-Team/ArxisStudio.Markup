using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>How a document should be parsed.</summary>
public sealed class XamlParseOptions
{
    /// <summary>Gets the options used when none are supplied.</summary>
    public static XamlParseOptions Default { get; } = new();

    /// <summary>
    /// Gets the document's location, attached to every diagnostic the parse produces.
    /// </summary>
    /// <remarks>
    /// Purely an identifier. Nothing here reads the filesystem, so this may name a document
    /// that exists only in memory.
    /// </remarks>
    public Uri? DocumentUri { get; init; }
}
