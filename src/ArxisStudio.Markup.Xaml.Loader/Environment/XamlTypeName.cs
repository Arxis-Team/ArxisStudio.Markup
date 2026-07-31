using System;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// A type as the document names it: a namespace URI and a local name, with any type arguments.
/// </summary>
/// <remarks>
/// The namespace URI, not the prefix. A prefix is only a local spelling, and resolving against
/// one would give different answers for documents that mean the same thing.
/// </remarks>
/// <param name="NamespaceUri">The XML namespace URI the name belongs to.</param>
/// <param name="LocalName">The type's local name.</param>
/// <param name="TypeArguments">The arguments of a generic type, empty when it has none.</param>
public readonly record struct XamlTypeName(
    string NamespaceUri,
    string LocalName,
    ImmutableArray<XamlTypeName> TypeArguments)
{
    /// <summary>Creates a name for a non-generic type.</summary>
    /// <param name="namespaceUri">The XML namespace URI.</param>
    /// <param name="localName">The type's local name.</param>
    public XamlTypeName(string namespaceUri, string localName)
        : this(namespaceUri, localName, [])
    {
    }

    /// <summary>Gets the XML namespace URI the name belongs to.</summary>
    public string NamespaceUri { get; } = NamespaceUri ?? throw new ArgumentNullException(nameof(NamespaceUri));

    /// <summary>Gets the type's local name.</summary>
    public string LocalName { get; } = LocalName ?? throw new ArgumentNullException(nameof(LocalName));

    /// <summary>Gets the arguments of a generic type.</summary>
    public ImmutableArray<XamlTypeName> TypeArguments { get; } =
        TypeArguments.IsDefault ? [] : TypeArguments;

    /// <summary>Gets a value indicating whether the name carries type arguments.</summary>
    public bool IsGeneric => !TypeArguments.IsEmpty;

    /// <summary>Returns the name in <c>{namespace}Local</c> form.</summary>
    /// <returns>A readable representation of the name.</returns>
    public override string ToString()
    {
        string arguments = IsGeneric
            ? "<" + string.Join(", ", TypeArguments.Select(static argument => argument.ToString())) + ">"
            : string.Empty;

        return $"{{{NamespaceUri}}}{LocalName}{arguments}";
    }
}
