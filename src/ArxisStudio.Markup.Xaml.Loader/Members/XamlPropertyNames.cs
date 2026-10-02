using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Which attribute of an element sets an Avalonia property, and what to call one that does not
/// exist yet.
/// </summary>
/// <remarks>
/// <para>
/// A property is written under its own name only on a type that has it. Every other property is
/// attached, and a document writes it as <c>Owner.Member</c>, with the owner named in a namespace in
/// scope: <c>Grid.Row</c>, <c>local:Dock.Edge</c>. An edit that wrote <c>Row="1"</c> on a button
/// wrote a member the button does not have, and a document that no longer loads.
/// </para>
/// <para>
/// Matched the way the document resolves it: the member by its name, the owner by its simple name
/// and the namespace its prefix is bound to where the attribute sits — which is what a type a
/// project and a library both call <c>Grid</c> would otherwise confuse.
/// </para>
/// </remarks>
internal static class XamlPropertyNames
{
    /// <summary>Finds the attribute of an element that sets a property, however its name is written.</summary>
    /// <param name="element">The element whose attributes to search.</param>
    /// <param name="property">The property.</param>
    /// <param name="targetType">The type of the object the element produced.</param>
    /// <returns>The attribute, or <see langword="null"/> when the element does not set the property.</returns>
    internal static XamlAttribute? Find(XamlElement element, AvaloniaProperty property, Type targetType)
    {
        foreach (XamlAttribute attribute in element.Attributes)
        {
            if (attribute is XamlNamespaceDeclaration
                || attribute.IsDirective
                || attribute.IsDesignTime
                || attribute.IsMarkupCompatibility)
            {
                continue;
            }

            if (Names(element, attribute.Name, property, targetType))
            {
                return attribute;
            }
        }

        return null;
    }

    /// <summary>Works out the name a new attribute setting a property is written under.</summary>
    /// <param name="editor">The editor the attribute will be written with, which declares the owner's namespace when the document lacks it.</param>
    /// <param name="element">The element the attribute goes on.</param>
    /// <param name="property">The property.</param>
    /// <param name="targetType">The type of the object the element produced.</param>
    /// <returns>The name.</returns>
    internal static XamlQualifiedName NameFor(
        XamlDocumentEditor editor,
        XamlElement element,
        AvaloniaProperty property,
        Type targetType)
    {
        if (IsOwnMember(property, targetType))
        {
            return XamlQualifiedName.Unprefixed(property.Name);
        }

        Type owner = property.OwnerType;

        // A namespace the document already binds to the owner, where the attribute sits, is the one
        // to use; failing that the owner's own published namespace, and failing that the plain
        // using: form, which names any type of any assembly the load can reach.
        string namespaceUri = NamespacesOf(owner)
            .FirstOrDefault(candidate => InScope(element, candidate))
            ?? NamespacesOf(owner).First();

        XamlQualifiedName named = editor.Qualify(element, namespaceUri, owner.Name);

        return new XamlQualifiedName(named.Prefix, $"{named.LocalName}.{property.Name}");
    }

    /// <summary>Reports whether a property is a member of the type itself, written without its owner.</summary>
    /// <remarks>
    /// Asked of the registry rather than of the property: an attached property a type has taken on
    /// with <c>AddOwner</c> — <c>TextBlock.FontSize</c> is <c>TextElement</c>'s — is that type's own
    /// as far as a document is concerned, and is written <c>FontSize</c>.
    /// </remarks>
    private static bool IsOwnMember(AvaloniaProperty property, Type targetType) =>
        ReferenceEquals(AvaloniaPropertyRegistry.Instance.FindRegistered(targetType, property.Name), property);

    private static bool Names(XamlElement element, XamlQualifiedName name, AvaloniaProperty property, Type targetType)
    {
        string local = name.LocalName;
        int dot = local.IndexOf('.', StringComparison.Ordinal);

        if (dot < 0)
        {
            return name.Prefix is null
                && string.Equals(local, property.Name, StringComparison.Ordinal)
                && IsOwnMember(property, targetType);
        }

        if (!string.Equals(local[(dot + 1)..], property.Name, StringComparison.Ordinal)
            || !string.Equals(local[..dot], property.OwnerType.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return element.NamespaceContext.LookupNamespace(name.Prefix) is { } namespaceUri
            && Maps(namespaceUri, property.OwnerType);
    }

    private static bool InScope(XamlElement element, string namespaceUri)
    {
        for (XamlNamespaceContext? context = element.NamespaceContext; context is not null; context = context.Parent)
        {
            foreach ((_, string declared) in context.Declarations)
            {
                if (string.Equals(declared, namespaceUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets the namespaces a document can name a type in, the published ones first.</summary>
    private static IEnumerable<string> NamespacesOf(Type type)
    {
        foreach (XmlnsDefinitionAttribute definition in Definitions(type.Assembly))
        {
            if (string.Equals(definition.ClrNamespace, type.Namespace, StringComparison.Ordinal))
            {
                yield return definition.XmlNamespace;
            }
        }

        yield return $"using:{type.Namespace}";
    }

    /// <summary>Reports whether a namespace a document binds names the CLR namespace a type is in.</summary>
    private static bool Maps(string namespaceUri, Type type)
    {
        const string Using = "using:";
        const string ClrNamespace = "clr-namespace:";

        if (namespaceUri.StartsWith(Using, StringComparison.Ordinal))
        {
            return string.Equals(namespaceUri[Using.Length..], type.Namespace, StringComparison.Ordinal);
        }

        if (namespaceUri.StartsWith(ClrNamespace, StringComparison.Ordinal))
        {
            string written = namespaceUri[ClrNamespace.Length..];
            int assembly = written.IndexOf(';', StringComparison.Ordinal);

            return string.Equals(assembly < 0 ? written : written[..assembly], type.Namespace, StringComparison.Ordinal);
        }

        return Definitions(type.Assembly).Any(definition =>
            string.Equals(definition.XmlNamespace, namespaceUri, StringComparison.Ordinal)
            && string.Equals(definition.ClrNamespace, type.Namespace, StringComparison.Ordinal));
    }

    private static IEnumerable<XmlnsDefinitionAttribute> Definitions(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttributes<XmlnsDefinitionAttribute>();
        }
        catch (Exception error) when (error is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            return [];
        }
    }
}
