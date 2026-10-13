using System;
using System.Collections.Generic;
using System.Linq;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a document names, in which scope a name is seen, and which elements read another by its name.
/// </summary>
/// <remarks>
/// <para>
/// A name — <c>x:Name</c> or <c>Name</c>, which <see cref="XamlElement.Identity"/> reads alike — belongs to the scope
/// it is written in: the document's own, or the nearest template's, whose content a load builds with a scope of its
/// own. That is the rule a binding's source is found by (ADR 0033), the scope an update keeps on the root, and what
/// decides how far a rebuild reaches (ADR 0036), so the three read it from here.
/// </para>
/// <para>
/// A binding reads an element by its name when it writes <c>ElementName</c>, or a path that starts with <c>#name</c> —
/// past a negation, as Avalonia's grammar reads it. It does so as an attribute's expression, nested in another, or as
/// an element written in a property element (<c>&lt;Binding ElementName="…" /&gt;</c>, a multi-binding's parts), whose
/// reader is the element the property element belongs to.
/// </para>
/// </remarks>
internal static class XamlNameScopes
{
    /// <summary>The names a binding is written under, as an extension or as an element.</summary>
    private static readonly string[] Bindings =
    [
        "Binding", "BindingExtension", "CompiledBinding", "CompiledBindingExtension",
        "ReflectionBinding", "ReflectionBindingExtension",
    ];

    /// <summary>The nearest template an element stands in — its own name scope; <see langword="null"/> outside templates.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The template element, or <see langword="null"/> when the element is in the document's own scope.</returns>
    internal static XamlElement? Template(XamlElement element) =>
        element.AncestorsAndSelf().OfType<XamlElement>().Skip(1).FirstOrDefault(IsTemplate);

    /// <summary>Reports whether an element is a template, whose content has a name scope of its own.</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> for a template written as an element.</returns>
    internal static bool IsTemplate(XamlElement element) =>
        !element.IsPropertyElementSyntax && element.Name.LocalName.EndsWith("Template", StringComparison.Ordinal);

    /// <summary>Finds the element a name stands for where another element sees it.</summary>
    /// <param name="document">The document both are elements of.</param>
    /// <param name="name">The name.</param>
    /// <param name="seenFrom">The element the name is read from, whose scope it is looked up in.</param>
    /// <returns>The first element of that scope with the name, or <see langword="null"/> when none has it.</returns>
    internal static XamlElement? Find(XamlDocument document, string name, XamlElement seenFrom)
    {
        XamlElement? scope = Template(seenFrom);

        return document.DescendantElements().FirstOrDefault(candidate =>
            string.Equals(candidate.Identity, name, StringComparison.Ordinal) &&
            ReferenceEquals(Template(candidate), scope));
    }

    /// <summary>Gets the names of a document's own scope, each with the element it stands for.</summary>
    /// <remarks>
    /// The root's name among them, and the names in its dictionaries, which a load registers too. Of two elements
    /// written with one name, the first: the second is a load's error, and a scope holds one object under a name.
    /// </remarks>
    /// <param name="document">The document.</param>
    /// <returns>Each name once, in document order.</returns>
    internal static IEnumerable<(string Name, XamlElement Element)> OfDocument(XamlDocument document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (XamlElement element in document.DescendantElements())
        {
            if (element.Identity is { Length: > 0 } name && Template(element) is null && seen.Add(name))
            {
                yield return (name, element);
            }
        }
    }

    /// <summary>Gets every element of a document that reads another by its name, with the name it reads.</summary>
    /// <param name="document">The document.</param>
    /// <returns>
    /// Each reading, in document order, with the attribute it is written in — none for a binding written as an
    /// element. An element that reads two names is there twice.
    /// </returns>
    internal static IEnumerable<(XamlElement Reader, string Name, XamlAttribute? Attribute)> Reads(XamlDocument document)
    {
        foreach (XamlElement element in document.DescendantElements())
        {
            // A binding written as an element is read by the element whose property element holds it.
            XamlElement reader = IsBinding(element.Name.LocalName) && Owner(element) is { } owner ? owner : element;

            if (IsBinding(element.Name.LocalName) && Named(element) is { } written)
            {
                yield return (reader, written, null);
            }

            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute.GetValue() is XamlMarkupExtensionValue extension)
                {
                    foreach (string name in ReadBy(extension))
                    {
                        // A binding written as an element is not its attributes' to write.
                        yield return (reader, name, ReferenceEquals(reader, element) ? attribute : null);
                    }
                }
            }
        }
    }

    /// <summary>Gets the names an expression reads an element by, nested expressions included.</summary>
    /// <param name="extension">The expression.</param>
    /// <returns>The names, as written.</returns>
    internal static IEnumerable<string> ReadBy(XamlMarkupExtensionValue extension)
    {
        if (IsBinding(extension.TypeName.LocalName) && Named(extension) is { } name)
        {
            yield return name;
        }

        // A converter's parameter or a fallback written as another expression.
        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            if (argument.Value is XamlMarkupExtensionValue nested)
            {
                foreach (string inner in ReadBy(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>The element a binding written as an element sets a property of: past property elements and multi-bindings.</summary>
    private static XamlElement? Owner(XamlElement binding)
    {
        for (XamlSyntaxNode? parent = binding.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement { IsPropertyElementSyntax: false } owner
                && !IsBinding(owner.Name.LocalName)
                && !string.Equals(owner.Name.LocalName, "MultiBinding", StringComparison.Ordinal))
            {
                return owner;
            }
        }

        return null;
    }

    /// <summary>The name a binding written as an expression reads its source by, if it reads one so.</summary>
    private static string? Named(XamlMarkupExtensionValue binding) =>
        Named(
            (binding.PositionalArguments.FirstOrDefault() ?? binding.GetArgument("Path"))?.Value,
            binding.GetArgument("Source") is not null,
            binding.GetArgument("ElementName")?.Value);

    /// <summary>The name a binding written as an element reads its source by, if it reads one so.</summary>
    private static string? Named(XamlElement binding) =>
        Named(
            Attribute(binding, "Path")?.GetValue(),
            Attribute(binding, "Source") is not null,
            Attribute(binding, "ElementName")?.GetValue());

    /// <summary>
    /// The name a binding reads its source by, in the order Avalonia builds one: a path that starts from an element
    /// names it whatever else is written, a <c>Source</c> is the source, and then <c>ElementName</c>.
    /// </summary>
    private static string? Named(XamlValue? path, bool hasSource, XamlValue? elementName)
    {
        if (path is XamlLiteralValue { Text: { } written } && NameInPath(written) is { } rooted)
        {
            return rooted;
        }

        return !hasSource && elementName is XamlLiteralValue { Text: { } text } && text.Trim() is { Length: > 0 } name
            ? name
            : null;
    }

    /// <summary>Gets the element a binding's path starts from by name — <c>#name</c>, past a negation — or none.</summary>
    /// <param name="path">The path as written, or <see langword="null"/>.</param>
    /// <returns>The name, or <see langword="null"/> when the path starts anywhere else.</returns>
    internal static string? NameInPath(string? path)
    {
        string rest = (path ?? string.Empty).Trim().TrimStart('!').TrimStart();

        if (!rest.StartsWith('#'))
        {
            return null;
        }

        int end = rest.IndexOfAny(['.', '['], 1);
        string name = (end < 0 ? rest[1..] : rest[1..end]).Trim();

        return name.Length > 0 ? name : null;
    }

    private static XamlAttribute? Attribute(XamlElement element, string localName) =>
        element.Attributes.FirstOrDefault(attribute => attribute is not XamlNamespaceDeclaration
            && !attribute.IsDirective
            && string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    private static bool IsBinding(string localName) => Bindings.Contains(localName, StringComparer.Ordinal);
}
