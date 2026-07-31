using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Finds the includes one document declares.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is syntactic. An element is an include because of what it is called and because
/// it has a <c>Source</c>, not because anything resolved a type — this package has no CLR
/// metadata and never will. Loading what the includes point at, and applying the resources
/// inside, belongs to the loader.
/// </para>
/// <para>
/// Includes are found wherever they appear, including inside merged dictionaries, because the
/// search is over every descendant rather than over a fixed set of expected positions.
/// </para>
/// </remarks>
public static class XamlResourceAnalyzer
{
    private const string SourceAttribute = "Source";

    /// <summary>Finds every include a document declares, in source order.</summary>
    /// <param name="document">The document to scan.</param>
    /// <returns>The references, with their sources resolved against the document's base URI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<XamlResourceReference> Discover(XamlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Discover(document, out _);
    }

    /// <summary>Finds every include a document declares, reporting anything unresolvable.</summary>
    /// <param name="document">The document to scan.</param>
    /// <param name="diagnostics">Diagnostics for includes that could not be resolved.</param>
    /// <returns>The references, in source order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<XamlResourceReference> Discover(
        XamlDocument document,
        out ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);

        ImmutableArray<XamlResourceReference>.Builder references =
            ImmutableArray.CreateBuilder<XamlResourceReference>();

        var reported = new List<MarkupDiagnostic>();

        foreach (XamlElement element in document.DescendantElements())
        {
            if (!TryGetKind(element, out XamlResourceReferenceKind kind))
            {
                continue;
            }

            XamlAttribute? source = element.GetAttribute(SourceAttribute);

            if (source is null || !source.HasValue)
            {
                reported.Add(MarkupDiagnostic.Parse(
                    XamlDiagnosticCodes.MissingIncludeSource,
                    $"<{element.Name}> has no Source attribute, so it includes nothing.",
                    MarkupDiagnosticSeverity.Warning,
                    document.Uri,
                    element.NameSpan));

                continue;
            }

            string text = source.GetValueText();

            // A Source written as a markup extension is resolved at runtime, not here. It is
            // recorded so it survives a save, but it cannot contribute an edge to the graph.
            if (source.GetValue() is not XamlLiteralValue)
            {
                references.Add(new XamlResourceReference(element, kind, text, resolvedUri: null));

                continue;
            }

            if (!XamlUri.TryResolve(text, document.BaseUri, out Uri? resolved))
            {
                reported.Add(MarkupDiagnostic.Parse(
                    XamlDiagnosticCodes.UnresolvedIncludeUri,
                    document.BaseUri is null
                        ? $"'{text}' is relative, and the document has no base URI to resolve it against."
                        : $"'{text}' could not be resolved against '{document.BaseUri}'.",
                    MarkupDiagnosticSeverity.Warning,
                    document.Uri,
                    source.ValueSpan));
            }

            references.Add(new XamlResourceReference(element, kind, text, resolved));
        }

        diagnostics = [.. reported];

        return references.ToImmutable();
    }

    /// <summary>
    /// Decides whether an element is an include, by its local name alone.
    /// </summary>
    /// <remarks>
    /// The namespace is not checked. Avalonia XAML puts these in the default namespace, but a
    /// document is free to bind that namespace to any URI, and demanding a particular one
    /// would make discovery fail on perfectly ordinary files.
    /// </remarks>
    private static bool TryGetKind(XamlElement element, out XamlResourceReferenceKind kind)
    {
        switch (element.Name.LocalName)
        {
            case "ResourceInclude":
                kind = XamlResourceReferenceKind.ResourceInclude;

                return true;

            case "StyleInclude":
                kind = XamlResourceReferenceKind.StyleInclude;

                return true;

            default:
                kind = default;

                return false;
        }
    }
}
