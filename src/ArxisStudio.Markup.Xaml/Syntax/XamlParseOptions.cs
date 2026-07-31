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

    /// <summary>
    /// Gets the URI relative includes are resolved against, defaulting to
    /// <see cref="DocumentUri"/>.
    /// </summary>
    /// <remarks>
    /// The two differ when a document's identity is not where it logically lives — an unsaved
    /// buffer standing in for a file, or a document assembled in memory whose relative includes
    /// are meant to resolve as if it sat in a particular folder.
    /// </remarks>
    public Uri? BaseUri { get; init; }
}
