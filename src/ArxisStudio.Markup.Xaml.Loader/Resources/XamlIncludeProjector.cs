using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Resolves a document's includes through the environment and hands back a projection of the
/// document with their content in place.
/// </summary>
/// <remarks>
/// <para>
/// The contract requires includes to work through the supplied resolvers, so that a document
/// assembled in memory or an unsaved edit resolves like anything else. Avalonia resolves an
/// include's <c>Source</c> during the load, through the asset loader in its service locator, and
/// throws if it cannot find it; there is no seam to substitute afterwards and no public way to
/// put a bridging asset loader in front. <c>docs/adr/0005-resource-includes.md</c> records both
/// dead ends and why this is the route instead.
/// </para>
/// <para>
/// Nothing here writes anything back. The document keeps its own text, and the projection —
/// which exists only for the duration of a load — carries the map that turns the positions
/// Avalonia reports against the projected text back into real positions in real files.
/// </para>
/// <para>
/// An include this cannot resolve, or cannot splice without changing what the markup means, is
/// left exactly as written. Avalonia's own asset loader then gets its chance at it, which is
/// what it would have had if none of this existed.
/// </para>
/// </remarks>
internal static class XamlIncludeProjector
{
    /// <summary>Projects a document's includes into its text.</summary>
    /// <param name="document">The document to project.</param>
    /// <param name="environment">The environment whose resource resolver finds the includes.</param>
    /// <param name="diagnostics">Collects everything noticed on the way.</param>
    /// <param name="cancellationToken">A token to observe while resolving and reading.</param>
    /// <returns>
    /// The projection, which is an identity projection when the document includes nothing this
    /// could resolve.
    /// </returns>
    internal static async ValueTask<TextProjection> ProjectAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var state = new State(environment, diagnostics);

        // The document being projected is on its own include path from the start, so a file
        // that includes itself is a cycle rather than an infinite descent.
        if (document.Uri is not null)
        {
            state.Path.Add(XamlUri.ToKey(document.Uri));
        }

        // Everything the document itself declares is already in scope where its includes sit.
        if (document.Root is { } root)
        {
            foreach ((string prefix, string namespaceUri) in DeclarationsOf(root))
            {
                state.Scope[prefix] = namespaceUri;
            }
        }

        return await ProjectAsync(document, state, isIncluded: false, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<TextProjection> ProjectAsync(
        XamlDocument document,
        State state,
        bool isIncluded,
        CancellationToken cancellationToken)
    {
        ImmutableArray<XamlResourceReference> references =
            XamlResourceAnalyzer.Discover(document, out ImmutableArray<MarkupDiagnostic> discovery);

        state.Diagnostics.AddRange(discovery);

        if (references.IsEmpty && !isIncluded)
        {
            return TextProjection.Identity(document.SourceText, document.Uri);
        }

        var builder = new TextProjectionBuilder(document.SourceText, document.Uri);

        foreach (XamlResourceReference reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A Source written as a markup extension, or a relative one with no base URI to
            // resolve it against. Discovery has already said so where there was anything to
            // say; either way the include goes to Avalonia as written.
            if (reference.ResolvedUri is not { } uri)
            {
                continue;
            }

            XamlDocument? included =
                await OpenAsync(reference, uri, document, state, cancellationToken).ConfigureAwait(false);

            if (included?.Root is not { } root || !Hoist(reference, root, document, state))
            {
                continue;
            }

            state.Path.Add(XamlUri.ToKey(uri));

            TextProjection inner;

            try
            {
                inner = await ProjectAsync(included, state, isIncluded: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                state.Path.RemoveAt(state.Path.Count - 1);
            }

            // The included document's root element stands in for the include. Everything around
            // it — an XML declaration, leading comments, the trailing newline — belongs to that
            // file rather than to this position in this one.
            builder.Replace(reference.Element.Span, inner, inner.GetProjectedSpan(root.Span));
        }

        Rebind(document, builder, state, isIncluded);

        return builder.ToProjection();
    }

    /// <summary>
    /// Moves namespace declarations to where the projected text may keep them.
    /// </summary>
    /// <remarks>
    /// Avalonia's XAML parser accepts <c>xmlns</c> only on the root element, so a document
    /// spliced in below the root cannot bring its own declarations with it. They are stripped
    /// from the fragment and added to the root of the document that is actually loaded, which
    /// leaves every name in the fragment resolving to what it resolved to before.
    /// </remarks>
    private static void Rebind(
        XamlDocument document,
        TextProjectionBuilder builder,
        State state,
        bool isIncluded)
    {
        if (document.Root is not { } root)
        {
            return;
        }

        if (isIncluded)
        {
            foreach (XamlNamespaceDeclaration declaration in root.NamespaceDeclarations)
            {
                builder.Replace(WithLeadingSpace(document.SourceText, declaration.Span), string.Empty);
            }

            return;
        }

        if (state.Hoisted.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();

        foreach ((string prefix, string namespaceUri) in state.Hoisted)
        {
            text.Append(prefix.Length == 0 ? " xmlns=\"" : $" xmlns:{prefix}=\"")
                .Append(namespaceUri)
                .Append('"');
        }

        // Straight after the element name, where an author would have written them, and before
        // any attribute so that nothing already on the tag has to move relative to anything else.
        builder.Replace(new TextSpan(root.NameSpan.End, 0), text.ToString());
    }

    /// <summary>
    /// Decides whether an included document's namespaces can be moved to the loaded root, and
    /// records them when they can.
    /// </summary>
    private static bool Hoist(
        XamlResourceReference reference,
        XamlElement root,
        XamlDocument document,
        State state)
    {
        Dictionary<string, string> declarations = DeclarationsOf(root);

        foreach ((string prefix, string namespaceUri) in declarations)
        {
            if (!state.Scope.TryGetValue(prefix, out string? existing)
                || string.Equals(existing, namespaceUri, StringComparison.Ordinal))
            {
                continue;
            }

            // Both files are fine on their own and cannot both be right once merged. Renaming
            // the prefix would mean rewriting every name and every markup extension that uses
            // it, so the include is left for Avalonia and the caller is told why.
            state.Diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.IncludeNamespaceConflict,
                $"'{reference.SourceText}' binds the prefix '{(prefix.Length == 0 ? "xmlns" : prefix)}' to " +
                $"'{namespaceUri}', which is already bound to '{existing}'. The include was left as written.",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                reference.Span));

            return false;
        }

        foreach ((string prefix, string namespaceUri) in declarations)
        {
            if (state.Scope.TryAdd(prefix, namespaceUri))
            {
                // Only a prefix nothing already binds needs adding to the loaded root; one that
                // is in scope with the same URI is already there.
                state.Hoisted[prefix] = namespaceUri;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves and parses what an include points at, or explains why the include is being left
    /// alone.
    /// </summary>
    private static async ValueTask<XamlDocument?> OpenAsync(
        XamlResourceReference reference,
        Uri uri,
        XamlDocument document,
        State state,
        CancellationToken cancellationToken)
    {
        if (state.Path.Contains(XamlUri.ToKey(uri), StringComparer.Ordinal))
        {
            state.Diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.IncludeCycle,
                $"'{reference.SourceText}' is already being included further up the chain. " +
                "The include was left as written rather than expanded for ever.",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                reference.Span));

            return null;
        }

        XamlResource? resource;

        try
        {
            resource = await state.Environment.ResourceResolver
                .ResolveAsync(uri, document.BaseUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            state.Diagnostics.Add(Unreadable(reference, document, error));

            return null;
        }

        if (resource is null)
        {
            // Not an error. Avalonia's asset loader knows about resources compiled into
            // assemblies this library was never handed, and it is about to get its turn.
            state.Diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.IncludeNotProjected,
                $"No resolver in the environment knew '{reference.SourceText}', so the include was left " +
                "for Avalonia's own asset loader to resolve.",
                MarkupDiagnosticSeverity.Info,
                document.Uri,
                reference.Span));

            return null;
        }

        XamlDocument included;

        try
        {
            included = await ReadAsync(resource, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            state.Diagnostics.Add(Unreadable(reference, document, error));

            return null;
        }

        if (included.Root is null || !included.IsWellFormed)
        {
            state.Diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.MalformedInclude,
                $"'{reference.SourceText}' did not parse cleanly, so the include was left as written.",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                reference.Span));

            // The included document's own errors say what is actually wrong with it, and they
            // carry its URI, so a caller can put them on the right file.
            state.Diagnostics.AddRange(included.GetDiagnostics().Where(static diagnostic => diagnostic.IsError));

            return null;
        }

        return included;
    }

    /// <summary>Reads and parses a resolved resource, once per document per projection.</summary>
    private static async ValueTask<XamlDocument> ReadAsync(
        XamlResource resource,
        State state,
        CancellationToken cancellationToken)
    {
        string key = XamlUri.ToKey(resource.Uri);

        if (state.Parsed.TryGetValue(key, out XamlDocument? cached))
        {
            return cached;
        }

        SourceText text = await resource.ReadTextAsync(cancellationToken).ConfigureAwait(false);

        // The included document's own URI is what its relative includes resolve against, so a
        // chain of files in different folders each resolves from where it actually lives.
        var document = XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = resource.Uri });

        state.Parsed[key] = document;

        return document;
    }

    /// <summary>Reads an element's namespace declarations, the default one keyed by an empty prefix.</summary>
    private static Dictionary<string, string> DeclarationsOf(XamlElement element)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (XamlNamespaceDeclaration declaration in element.NamespaceDeclarations)
        {
            declarations[declaration.Prefix ?? string.Empty] = declaration.GetNamespaceUri();
        }

        return declarations;
    }

    /// <summary>
    /// Extends an attribute's span back over the whitespace that separates it from what precedes
    /// it, so that removing it does not leave the gap behind.
    /// </summary>
    private static TextSpan WithLeadingSpace(SourceText text, TextSpan span)
    {
        int start = span.Start;

        while (start > 0 && char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        return TextSpan.FromBounds(start, span.End);
    }

    private static MarkupDiagnostic Unreadable(
        XamlResourceReference reference,
        XamlDocument document,
        Exception error) =>
        MarkupDiagnostic.Resolution(
            XamlLoaderDiagnosticCodes.UnreadableInclude,
            $"'{reference.SourceText}' could not be read: {error.Message}",
            MarkupDiagnosticSeverity.Warning,
            document.Uri,
            reference.Span);

    /// <summary>What one projection run carries across the documents it walks.</summary>
    private sealed class State(XamlLoadEnvironment environment, List<MarkupDiagnostic> diagnostics)
    {
        public XamlLoadEnvironment Environment { get; } = environment;

        public List<MarkupDiagnostic> Diagnostics { get; } = diagnostics;

        /// <summary>Documents already parsed, so a diamond of includes is read once.</summary>
        public Dictionary<string, XamlDocument> Parsed { get; } = new(StringComparer.Ordinal);

        /// <summary>The chain of documents currently being expanded, for cycle detection.</summary>
        public List<string> Path { get; } = [];

        /// <summary>Every prefix bound anywhere in the projection, and what it binds to.</summary>
        public Dictionary<string, string> Scope { get; } = new(StringComparer.Ordinal);

        /// <summary>The declarations that have to be added to the loaded document's root.</summary>
        public Dictionary<string, string> Hoisted { get; } = new(StringComparer.Ordinal);
    }
}
