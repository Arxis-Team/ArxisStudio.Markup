using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The namespace declarations in scope at a point in the document.
/// </summary>
/// <remarks>
/// <para>
/// Contexts form a chain, one link per element that declares anything. A lookup walks outwards
/// until it finds the prefix, so an inner declaration shadows an outer one exactly as XML
/// requires — including re-binding a prefix to a different URI part-way down a document.
/// </para>
/// <para>
/// Unknown namespace URIs are kept and resolved like any other. This package has no list of
/// namespaces it accepts; a URI it has never seen is simply a URI.
/// </para>
/// </remarks>
public sealed class XamlNamespaceContext
{
    /// <summary>The key the default namespace is stored under, which is never a legal prefix.</summary>
    private const string DefaultPrefixKey = "";

    private readonly ImmutableDictionary<string, string> _declarations;

    private XamlNamespaceContext(XamlNamespaceContext? parent, ImmutableDictionary<string, string> declarations)
    {
        Parent = parent;
        _declarations = declarations;
    }

    /// <summary>Gets an empty context, in which nothing but the fixed <c>xml</c> prefix resolves.</summary>
    public static XamlNamespaceContext Empty { get; } =
        new(null, ImmutableDictionary<string, string>.Empty);

    /// <summary>Gets the enclosing context, or <see langword="null"/> at the document root.</summary>
    public XamlNamespaceContext? Parent { get; }

    /// <summary>Gets the declarations made at this level, keyed by prefix, with the default namespace under an empty key.</summary>
    public IReadOnlyDictionary<string, string> Declarations => _declarations;

    /// <summary>Gets a value indicating whether this level declares anything.</summary>
    public bool IsEmpty => _declarations.IsEmpty;

    /// <summary>Creates a child context that adds declarations to this one.</summary>
    /// <param name="declarations">
    /// The prefix-to-URI pairs declared at the new level, with the default namespace under an
    /// empty or <see langword="null"/> prefix.
    /// </param>
    /// <returns>
    /// The child context, or this one when <paramref name="declarations"/> is empty, so that
    /// elements which declare nothing add no link to the chain.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="declarations"/> is <see langword="null"/>.</exception>
    public XamlNamespaceContext Push(IReadOnlyCollection<KeyValuePair<string?, string>> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        if (declarations.Count == 0)
        {
            return this;
        }

        ImmutableDictionary<string, string>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach ((string? prefix, string namespaceUri) in declarations)
        {
            // A repeated prefix on one element is malformed XML; the parser reports it and the
            // last one written wins, which keeps lookup total rather than throwing here.
            builder[prefix ?? DefaultPrefixKey] = namespaceUri;
        }

        return new XamlNamespaceContext(this, builder.ToImmutable());
    }

    /// <summary>Resolves a prefix to the namespace URI in scope for it.</summary>
    /// <param name="prefix">
    /// The prefix, or <see langword="null"/> or empty for the default namespace.
    /// </param>
    /// <returns>
    /// The namespace URI, or <see langword="null"/> when the prefix is not declared anywhere
    /// in scope. An undeclared prefix is a fact about the document, not an error to throw on.
    /// </returns>
    public string? LookupNamespace(string? prefix)
    {
        string key = prefix ?? DefaultPrefixKey;

        // The XML specification fixes these two and forbids redeclaring them.
        if (string.Equals(key, "xml", StringComparison.Ordinal))
        {
            return XamlNamespaces.Xml;
        }

        if (string.Equals(key, "xmlns", StringComparison.Ordinal))
        {
            return XamlNamespaces.Xmlns;
        }

        for (XamlNamespaceContext? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope._declarations.TryGetValue(key, out string? namespaceUri))
            {
                // An empty URI undeclares the prefix, which XML permits for the default
                // namespace and for prefixes since Namespaces 1.1.
                return namespaceUri.Length == 0 ? null : namespaceUri;
            }
        }

        return null;
    }

    /// <summary>Resolves the name of an element or attribute against the namespaces in scope.</summary>
    /// <param name="name">The name as written.</param>
    /// <param name="namespaceUri">The namespace URI, when the name's prefix is in scope.</param>
    /// <returns><see langword="true"/> if the name resolves.</returns>
    /// <remarks>
    /// An unprefixed <em>attribute</em> is in no namespace, unlike an unprefixed element, which
    /// takes the default namespace. Callers must say which they are resolving.
    /// </remarks>
    public bool TryResolveElementName(XamlQualifiedName name, [NotNullWhen(true)] out string? namespaceUri)
    {
        namespaceUri = LookupNamespace(name.Prefix);

        return namespaceUri is not null;
    }

    /// <summary>Resolves an attribute name against the namespaces in scope.</summary>
    /// <param name="name">The name as written.</param>
    /// <param name="namespaceUri">The namespace URI, when the name is prefixed and its prefix is in scope.</param>
    /// <returns><see langword="true"/> if the name is prefixed and resolves.</returns>
    public bool TryResolveAttributeName(XamlQualifiedName name, [NotNullWhen(true)] out string? namespaceUri)
    {
        if (!name.HasPrefix)
        {
            // Per the XML namespaces specification, an unprefixed attribute is in no namespace
            // regardless of any default declaration.
            namespaceUri = null;

            return false;
        }

        namespaceUri = LookupNamespace(name.Prefix);

        return namespaceUri is not null;
    }

    /// <summary>Finds a prefix currently bound to a namespace URI.</summary>
    /// <param name="namespaceUri">The namespace URI to look for.</param>
    /// <returns>
    /// The innermost prefix bound to it, an empty string for the default namespace, or
    /// <see langword="null"/> when nothing in scope is bound to it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="namespaceUri"/> is <see langword="null"/>.</exception>
    public string? LookupPrefix(string namespaceUri)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);

        var shadowed = new HashSet<string>(StringComparer.Ordinal);

        for (XamlNamespaceContext? scope = this; scope is not null; scope = scope.Parent)
        {
            foreach ((string prefix, string declared) in scope._declarations)
            {
                // An inner declaration of the same prefix hides this one, so a prefix already
                // seen further in cannot be reported as bound to this URI.
                if (shadowed.Add(prefix) && string.Equals(declared, namespaceUri, StringComparison.Ordinal))
                {
                    return prefix;
                }
            }
        }

        return null;
    }

    /// <summary>Gets every prefix in scope with the URI it currently resolves to.</summary>
    /// <returns>
    /// The effective declarations, innermost first, with shadowed outer declarations omitted.
    /// </returns>
    public IReadOnlyDictionary<string, string> GetInScopeDeclarations()
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);

        for (XamlNamespaceContext? scope = this; scope is not null; scope = scope.Parent)
        {
            foreach ((string prefix, string declared) in scope._declarations)
            {
                // TryAdd keeps the innermost binding, because outer scopes are visited later.
                effective.TryAdd(prefix, declared);
            }
        }

        return effective;
    }
}
