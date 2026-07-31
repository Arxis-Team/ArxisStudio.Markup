using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// URI handling for resource includes.
/// </summary>
/// <remarks>
/// Resolution here is arithmetic on URIs and nothing more. It touches no filesystem, opens no
/// assembly and asks nothing about whether the result exists — a document may perfectly well
/// include one that has not been written yet, and an editor has to cope with that.
/// </remarks>
public static class XamlUri
{
    /// <summary>The scheme Avalonia uses for resources embedded in an assembly.</summary>
    public const string AvaloniaResourceScheme = "avares";

    /// <summary>
    /// Renders a URI as it should be shown and written back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Uri.ToString"/> lower-cases the host, which is right for a DNS name and wrong
    /// for an <c>avares://</c> URI, where the host is an assembly name and assembly names are
    /// case-sensitive. Left alone, <c>avares://Controls/Themes/Generic.axaml</c> comes back as
    /// <c>avares://controls/...</c> — a different assembly as far as Avalonia is concerned.
    /// </para>
    /// <para>
    /// <see cref="Uri.OriginalString"/> is what the caller actually wrote, so it is used for
    /// any absolute URI rather than only for this one scheme; there is no case where showing
    /// the author's own text is worse.
    /// </para>
    /// </remarks>
    /// <param name="uri">The URI to render.</param>
    /// <returns>The URI as written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static string ToDisplayString(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri ? uri.OriginalString : uri.ToString();
    }

    /// <summary>
    /// Produces the key two URIs are the same document under.
    /// </summary>
    /// <remarks>
    /// Ordinary schemes get <see cref="Uri.AbsoluteUri"/>, which normalises away the
    /// differences that do not matter. The <c>avares</c> scheme gets its original text,
    /// because its host is an assembly name: treating <c>avares://Controls/X</c> and
    /// <c>avares://controls/X</c> as one document would silently merge two different
    /// assemblies' resources.
    /// </remarks>
    /// <param name="uri">The URI to key.</param>
    /// <returns>The identity key, compared ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static string ToKey(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return uri.OriginalString;
        }

        return IsAvaloniaResource(uri) ? uri.OriginalString : uri.AbsoluteUri;
    }

    /// <summary>
    /// Compares URIs the way this package identifies documents, preserving the case of an
    /// <c>avares://</c> assembly name.
    /// </summary>
    public static IEqualityComparer<Uri> Comparer { get; } = new KeyComparer();

    private sealed class KeyComparer : IEqualityComparer<Uri>
    {
        public bool Equals(Uri? x, Uri? y) =>
            x is null || y is null ? ReferenceEquals(x, y) : string.Equals(ToKey(x), ToKey(y), StringComparison.Ordinal);

        public int GetHashCode(Uri obj) => ToKey(obj).GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>Gets a value indicating whether a URI addresses an embedded Avalonia resource.</summary>
    /// <param name="uri">The URI to test.</param>
    /// <returns><see langword="true"/> if its scheme is <c>avares</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static bool IsAvaloniaResource(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, AvaloniaResourceScheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the assembly name out of an <c>avares://</c> URI.
    /// </summary>
    /// <remarks>
    /// From the original text rather than <see cref="Uri.Host"/>, which lower-cases it. Assembly
    /// names are case-sensitive, so the parsed host names a different assembly.
    /// </remarks>
    /// <param name="uri">The URI to read.</param>
    /// <returns>The assembly name, or <see langword="null"/> when the URI names no assembly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static string? GetAssemblyName(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!IsAvaloniaResource(uri))
        {
            return null;
        }

        const string SchemePrefix = AvaloniaResourceScheme + "://";

        string original = uri.OriginalString;

        if (!original.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        string rest = original[SchemePrefix.Length..];
        int slash = rest.IndexOf('/', StringComparison.Ordinal);
        string name = slash < 0 ? rest : rest[..slash];

        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Resolves an include's source against the document that declared it.
    /// </summary>
    /// <remarks>
    /// An absolute source stands on its own and is returned unchanged, whatever its scheme —
    /// <c>avares://</c> included. Only a relative one is combined with the base, and only when
    /// there is a base to combine it with.
    /// </remarks>
    /// <param name="source">The <c>Source</c> attribute's text.</param>
    /// <param name="baseUri">The declaring document's base URI, if it has one.</param>
    /// <param name="resolved">The resolved URI.</param>
    /// <returns><see langword="true"/> if the source resolved.</returns>
    public static bool TryResolve(string source, Uri? baseUri, [NotNullWhen(true)] out Uri? resolved)
    {
        resolved = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? absolute))
        {
            resolved = absolute;

            return true;
        }

        if (baseUri is null || !baseUri.IsAbsoluteUri)
        {
            // A relative include in a document with no location of its own cannot be placed.
            // That is a fact about the caller's setup, not a malformed document.
            return false;
        }

        return Uri.TryCreate(baseUri, source, out resolved);
    }
}
