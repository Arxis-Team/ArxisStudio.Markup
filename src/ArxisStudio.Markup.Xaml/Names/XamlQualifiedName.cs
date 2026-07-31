using System;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A name as it is written in the document: an optional prefix and a local name.
/// </summary>
/// <remarks>
/// <para>
/// This is a syntactic name, not a resolved one. The prefix is kept exactly as the document
/// spells it so that writing the name back reproduces the original text; what the prefix
/// <em>means</em> is a separate question answered by <see cref="XamlNamespaceContext"/>.
/// </para>
/// <para>
/// A dotted local name such as <c>Grid.Row</c> is left whole. Deciding that it names an owner
/// and a member — let alone which kind of member — is not something this package can know.
/// </para>
/// </remarks>
/// <param name="Prefix">The prefix, or <see langword="null"/> when the name is unprefixed.</param>
/// <param name="LocalName">The part after the prefix.</param>
public readonly record struct XamlQualifiedName(string? Prefix, string LocalName)
{
    /// <summary>Gets the prefix, or <see langword="null"/> when the name is unprefixed.</summary>
    public string? Prefix { get; } = string.IsNullOrEmpty(Prefix) ? null : Prefix;

    /// <summary>Gets the part after the prefix.</summary>
    public string LocalName { get; } = LocalName ?? throw new ArgumentNullException(nameof(LocalName));

    /// <summary>Gets a value indicating whether the name carries a prefix.</summary>
    public bool HasPrefix => Prefix is not null;

    /// <summary>Creates an unprefixed name.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The name.</returns>
    public static XamlQualifiedName Unprefixed(string localName) => new(null, localName);

    /// <summary>Splits a written name at its first colon.</summary>
    /// <remarks>
    /// A name with more than one colon is not valid XML. It is kept whole rather than
    /// reinterpreted, because discarding part of it would lose source the caller may still
    /// want written back.
    /// </remarks>
    /// <param name="text">The name as written.</param>
    /// <returns>The parsed name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlQualifiedName Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int colon = text.IndexOf(':', StringComparison.Ordinal);

        return colon < 0
            ? new XamlQualifiedName(null, text)
            : new XamlQualifiedName(text[..colon], text[(colon + 1)..]);
    }

    /// <summary>Determines whether this name has the given local name and no prefix.</summary>
    /// <param name="localName">The local name to compare against.</param>
    /// <returns><see langword="true"/> if the name matches.</returns>
    public bool IsUnprefixed(string localName) =>
        Prefix is null && string.Equals(LocalName, localName, StringComparison.Ordinal);

    /// <summary>Returns the name as it would be written.</summary>
    /// <returns>The name in <c>prefix:local</c> form, or just the local name.</returns>
    public override string ToString() => Prefix is null ? LocalName : $"{Prefix}:{LocalName}";
}
