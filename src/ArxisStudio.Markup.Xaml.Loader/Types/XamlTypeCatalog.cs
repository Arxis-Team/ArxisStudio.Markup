using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The types a set of assemblies offers a document, said in names only.
/// </summary>
/// <remarks>
/// <para>
/// What a toolbox lists and what a data panel offers as a document's data type are the same question
/// asked of different assemblies: which types can a document name, in which namespace, and what can
/// it do with each — write it as an element, put children in it, bind to it. This answers it once, in
/// the terms the loader itself resolves names in: a type's namespace is the one its assembly maps its
/// CLR namespace to, as <see cref="XamlTypeResolver"/> reads the mapping, and <c>using:</c> where the
/// assembly maps none.
/// </para>
/// <para>
/// Nothing in a catalog holds a type or an assembly — see <see cref="XamlTypeEntry"/> — so a catalog
/// read from a generation of a project's code does not keep that generation alive. The other side of
/// that is that a catalog is a reading, not a view: a build that adds a control is a new catalog.
/// </para>
/// <para>
/// What is listed: public classes and interfaces that are not nested, not generic definitions, not
/// static, and not attributes, delegates or what a compiler generates — the types a document can name
/// at all. <see cref="XamlTypeEntry.Kinds"/> says which of them it can write as elements, which are
/// controls and which are data; choosing among them is the tool's.
/// </para>
/// </remarks>
public sealed class XamlTypeCatalog
{
    private XamlTypeCatalog(ImmutableArray<XamlTypeEntry> entries, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Entries = entries;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the types, ordered by name and then by full name.</summary>
    public ImmutableArray<XamlTypeEntry> Entries { get; }

    /// <summary>Gets what could not be read: types of an assembly whose dependencies are missing.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

    /// <summary>Reads the types a set of assemblies offers a document.</summary>
    /// <param name="assemblies">The assemblies to read. Each is read once, and a dynamic one not at all.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> is <see langword="null"/>, or holds <see langword="null"/>.</exception>
    public static XamlTypeCatalog Create(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var entries = new List<XamlTypeEntry>();
        var diagnostics = new List<MarkupDiagnostic>();

        foreach (Assembly assembly in assemblies.Distinct())
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));

            // A dynamic assembly has no exported types to ask for; what is built at run time is
            // nothing a document is written against.
            if (assembly.IsDynamic)
            {
                continue;
            }

            Read(assembly, entries, diagnostics);
        }

        return new XamlTypeCatalog(
            [
                .. entries
                    .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.FullName, StringComparer.Ordinal),
            ],
            [.. diagnostics]);
    }

    /// <summary>Finds the entry for a type by its full name.</summary>
    /// <param name="fullName">The CLR name with its namespace.</param>
    /// <returns>The entry, or <see langword="null"/> when the catalog has no such type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null"/>.</exception>
    public XamlTypeEntry? Find(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        return Entries.FirstOrDefault(entry => string.Equals(entry.FullName, fullName, StringComparison.Ordinal));
    }

    private static void Read(Assembly assembly, List<XamlTypeEntry> entries, List<MarkupDiagnostic> diagnostics)
    {
        string assemblyName = assembly.GetName().Name ?? string.Empty;
        (Type[] types, int unreadable) = ExportedTypes(assembly);

        ImmutableArray<XmlnsDefinitionAttribute> definitions = XamlNamespaceAttributes.DefinitionsOf(assembly);
        ImmutableArray<XmlnsPrefixAttribute> prefixes = XamlNamespaceAttributes.PrefixesOf(assembly);

        foreach (Type type in types)
        {
            try
            {
                if (Describe(type, assemblyName, definitions, prefixes) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception error) when (error is TypeLoadException or FileNotFoundException or FileLoadException)
            {
                // A base type or an interface from an assembly that is not there: the type exists and
                // nothing can be said about it.
                unreadable++;
            }
        }

        if (unreadable > 0)
        {
            diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.UnreadableTypes,
                $"{unreadable} public type(s) of assembly '{assemblyName}' could not be read, most likely " +
                "because an assembly they depend on is missing. The catalog lists the rest."));
        }
    }

    /// <summary>Gets an assembly's public types, and how many of them could not be loaded.</summary>
    private static (Type[] Types, int Unreadable) ExportedTypes(Assembly assembly)
    {
        try
        {
            return (assembly.GetExportedTypes(), 0);
        }
        catch (ReflectionTypeLoadException partial)
        {
            Type[] loaded = [.. partial.Types.OfType<Type>().Where(static type => type.IsPublic)];

            return (loaded, partial.Types.Count(static type => type is null));
        }
        catch (Exception error) when (error is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            return ([], 1);
        }
    }

    /// <summary>Describes a type a document can name, or nothing for one it cannot.</summary>
    private static XamlTypeEntry? Describe(
        Type type,
        string assemblyName,
        ImmutableArray<XmlnsDefinitionAttribute> definitions,
        ImmutableArray<XmlnsPrefixAttribute> prefixes)
    {
        if (!Nameable(type) || type.Namespace is not { Length: > 0 } clrNamespace)
        {
            return null;
        }

        // The first mapping the assembly declares for the namespace, which is the one the resolver
        // meets first when it reads the document back.
        string xmlNamespace = definitions
            .FirstOrDefault(definition => string.Equals(definition.ClrNamespace, clrNamespace, StringComparison.Ordinal))
            ?.XmlNamespace
            ?? "using:" + clrNamespace;

        string? prefix = prefixes
            .FirstOrDefault(suggestion => string.Equals(suggestion.XmlNamespace, xmlNamespace, StringComparison.Ordinal))
            ?.Prefix;

        return new XamlTypeEntry(
            type.FullName ?? clrNamespace + "." + type.Name,
            type.Name,
            clrNamespace,
            assemblyName,
            xmlNamespace,
            prefix is { Length: > 0 } ? prefix : null,
            KindsOf(type));
    }

    /// <summary>Reports whether a document can name a type at all.</summary>
    private static bool Nameable(Type type) =>
        !type.IsNested
        && (type.IsClass || type.IsInterface)
        && !type.IsGenericTypeDefinition
        && !(type.IsAbstract && type.IsSealed)
        && !typeof(Delegate).IsAssignableFrom(type)
        && !typeof(Attribute).IsAssignableFrom(type)
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        // What a markup compiler emits beside the types it compiled — CompiledAvaloniaXaml.!XamlLoader
        // and the like — is public and named so that no document can write it.
        && type.Name.All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static XamlTypeKinds KindsOf(Type type)
    {
        XamlTypeKinds kinds = XamlTypeKinds.None;

        if (type.IsClass && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            kinds |= XamlTypeKinds.Creatable;
        }

        if (!typeof(AvaloniaObject).IsAssignableFrom(type))
        {
            return kinds | XamlTypeKinds.Data;
        }

        if (!typeof(Control).IsAssignableFrom(type))
        {
            return kinds;
        }

        kinds |= XamlTypeKinds.Control;

        if (typeof(Panel).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.Panel;
        }

        if (typeof(ContentControl).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.ContentControl;
        }

        if (typeof(Decorator).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.Decorator;
        }

        if (typeof(ItemsControl).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.ItemsControl;
        }

        if (typeof(TemplatedControl).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.TemplatedControl;
        }

        if (typeof(UserControl).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.UserControl;
        }

        if (typeof(TopLevel).IsAssignableFrom(type))
        {
            kinds |= XamlTypeKinds.TopLevel;
        }

        if (XamlPopulateHook.Find(type) is not null)
        {
            kinds |= XamlTypeKinds.CompiledMarkup;
        }

        return kinds;
    }
}
