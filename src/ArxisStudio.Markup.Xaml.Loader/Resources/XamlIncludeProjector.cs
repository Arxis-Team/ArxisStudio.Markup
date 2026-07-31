using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        // A document two includes both reach is walked once per route, and what is wrong with
        // it is wrong with it once however many routes lead there.
        if (state.Described(document.Uri))
        {
            state.Diagnostics.AddRange(discovery);
        }

        if (references.IsEmpty && !isIncluded)
        {
            return TextProjection.Identity(document.SourceText, document.Uri);
        }

        var builder = new TextProjectionBuilder(document.SourceText, document.Uri);
        var spliced = new List<TextSpan>();

        foreach (XamlResourceReference reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Discovery reports every include anywhere in the document, which includes one
            // written inside another. The outer one is replaced wholesale, so the inner one is
            // already gone; asking to replace it as well describes two texts for one place.
            if (spliced.Exists(span => span.Contains(reference.Element.Span)))
            {
                continue;
            }

            // A Source written as a markup extension, or a relative one with no base URI to
            // resolve it against. Discovery has already said so where there was anything to
            // say; either way the include goes to Avalonia as written.
            if (reference.ResolvedUri is not { } uri)
            {
                Rebase(reference, builder, isIncluded);

                continue;
            }

            XamlDocument? included =
                await OpenAsync(reference, uri, document, state, cancellationToken).ConfigureAwait(false);

            if (included?.Root is not { } root || !Hoist(reference, root, included.Uri, document, state))
            {
                Rebase(reference, builder, isIncluded);

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
            spliced.Add(reference.Element.Span);
        }

        Rebind(document, builder, state, isIncluded);

        return builder.ToProjection();
    }

    /// <summary>
    /// Makes an include that is being left as written say where it points from its new home.
    /// </summary>
    /// <remarks>
    /// A relative <c>Source</c> means "beside the file it is written in". Once the file it is
    /// written in has been spliced into another one, Avalonia resolves it against the host's
    /// base URI instead, which is a different folder. Writing the URI it already resolved to
    /// into the projection keeps it pointing where the author meant, and the document itself
    /// still says what the author wrote.
    /// </remarks>
    private static void Rebase(XamlResourceReference reference, TextProjectionBuilder builder, bool isIncluded)
    {
        if (!isIncluded
            || reference.ResolvedUri is not { } uri
            || reference.Element.GetAttribute("Source")?.ValueSpan is not { } written
            || Uri.TryCreate(reference.SourceText, UriKind.Absolute, out _))
        {
            return;
        }

        builder.Replace(written, XamlUri.ToDisplayString(uri));
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
        Uri? includedUri,
        XamlDocument document,
        State state)
    {
        Dictionary<string, string> declarations =
            DeclarationsOf(root, includedUri is null ? null : XamlUri.GetAssemblyName(includedUri));

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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A resolver is the caller's own code and may fail in any way its source of
            // resources can. That is a resource this load could not have, which is a diagnostic,
            // not a reason to abandon a load that would otherwise have produced a tree.
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A resolver is the caller's own code and may fail in any way its source of
            // resources can. That is a resource this load could not have, which is a diagnostic,
            // not a reason to abandon a load that would otherwise have produced a tree.
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
            if (state.Described(included.Uri))
            {
                state.Diagnostics.AddRange(
                    included.GetDiagnostics().Where(static diagnostic => diagnostic.IsError));
            }

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
    /// <param name="element">The element whose declarations to read.</param>
    /// <param name="assembly">
    /// The assembly the element's document lives in, when it is known, so that declarations
    /// which mean "this document's own assembly" keep meaning that once moved.
    /// </param>
    private static Dictionary<string, string> DeclarationsOf(XamlElement element, string? assembly = null)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (XamlNamespaceDeclaration declaration in element.NamespaceDeclarations)
        {
            declarations[declaration.Prefix ?? string.Empty] = Qualify(declaration.GetNamespaceUri(), assembly);
        }

        return declarations;
    }

    /// <summary>
    /// Names the assembly a declaration only implied, when moving it would change which assembly
    /// that is.
    /// </summary>
    /// <remarks>
    /// <c>using:Some.Namespace</c> and <c>clr-namespace:Some.Namespace</c> both mean "in the
    /// assembly of the document this is written in". Hoisting such a declaration onto another
    /// document's root would silently repoint it at that document's assembly instead, so the
    /// assembly it meant is written out. Anything already absolute is left alone.
    /// </remarks>
    private static string Qualify(string namespaceUri, string? assembly)
    {
        const string Using = "using:";
        const string ClrNamespace = "clr-namespace:";

        if (assembly is null)
        {
            return namespaceUri;
        }

        if (namespaceUri.StartsWith(Using, StringComparison.Ordinal))
        {
            return $"{ClrNamespace}{namespaceUri[Using.Length..]};assembly={assembly}";
        }

        return namespaceUri.StartsWith(ClrNamespace, StringComparison.Ordinal)
            && !namespaceUri.Contains(";assembly=", StringComparison.Ordinal)
                ? $"{namespaceUri};assembly={assembly}"
                : namespaceUri;
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

        private HashSet<string> Reported { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Reports whether a document still has to have its own diagnostics collected, and
        /// records that it now has.
        /// </summary>
        /// <param name="uri">The document's URI, or <see langword="null"/> when it has none.</param>
        /// <returns><see langword="true"/> the first time a document is asked about.</returns>
        public bool Described(Uri? uri) => uri is null || Reported.Add(XamlUri.ToKey(uri));
    }
}
