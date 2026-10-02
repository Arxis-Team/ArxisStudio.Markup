using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArxisStudio.Markup.Xaml.Loader.TestControls;
using Avalonia.Controls;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The types a set of assemblies offers a document, read in the terms the loader resolves names in,
/// and held as names only.
/// </summary>
public sealed class TypeCatalogTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ControlsNamespace = "https://arxis.studio/test-controls";

    private static XamlTypeCatalog Catalog() =>
        XamlTypeCatalog.Create([typeof(Button).Assembly, typeof(CountedView).Assembly, typeof(TypeCatalogTests).Assembly]);

    private static XamlTypeEntry Entry(XamlTypeCatalog catalog, Type type) =>
        catalog.Find(type.FullName!) ?? throw new Xunit.Sdk.XunitException($"{type.FullName} is not in the catalog.");

    [Fact]
    public void AnEntrySaysWhereATypeIsWrittenAndWhatItIs()
    {
        XamlTypeCatalog catalog = Catalog();

        XamlTypeEntry button = Entry(catalog, typeof(Button));

        Assert.Equal("Button", button.Name);
        Assert.Equal(AvaloniaNamespace, button.XmlNamespace);
        Assert.Equal("Avalonia.Controls", button.AssemblyName);
        Assert.Equal(
            XamlTypeKinds.Creatable | XamlTypeKinds.Control | XamlTypeKinds.ContentControl | XamlTypeKinds.TemplatedControl,
            button.Kinds);

        Assert.Equal(XamlTypeKinds.Creatable | XamlTypeKinds.Control | XamlTypeKinds.Panel, Entry(catalog, typeof(StackPanel)).Kinds);
        Assert.True(Entry(catalog, typeof(Border)).Kinds.HasFlag(XamlTypeKinds.Decorator));
        Assert.True(Entry(catalog, typeof(ListBox)).Kinds.HasFlag(XamlTypeKinds.ItemsControl));
        Assert.True(Entry(catalog, typeof(Window)).Kinds.HasFlag(XamlTypeKinds.TopLevel));

        // A control of the project's, mapped by its assembly, with the prefix its assembly suggests.
        XamlTypeEntry counted = Entry(catalog, typeof(CountedView));

        Assert.Equal(ControlsNamespace, counted.XmlNamespace);
        Assert.Equal("tc", counted.SuggestedPrefix);
        Assert.True(counted.Kinds.HasFlag(XamlTypeKinds.UserControl));
        Assert.False(counted.Kinds.HasFlag(XamlTypeKinds.CompiledMarkup));

        // One whose markup was compiled into it is built from that markup by a session.
        Assert.True(Entry(catalog, typeof(LiveControl)).Kinds.HasFlag(XamlTypeKinds.CompiledMarkup));

        // And data, which is what a document binds to rather than places.
        Assert.Equal(XamlTypeKinds.Creatable | XamlTypeKinds.Data, Entry(catalog, typeof(GreetingModel)).Kinds);
    }

    [Fact]
    public void ATypeWhoseAssemblyMapsNoNamespaceIsWrittenWithUsing()
    {
        XamlTypeEntry entry = Entry(Catalog(), typeof(CatalogModel));

        Assert.Equal("using:" + typeof(CatalogModel).Namespace, entry.XmlNamespace);
        Assert.Null(entry.SuggestedPrefix);
        Assert.Equal(XamlTypeKinds.Data, entry.Kinds);
    }

    [Fact]
    public void WhatADocumentCannotNameIsLeftOut()
    {
        XamlTypeCatalog catalog = Catalog();

        Assert.Null(catalog.Find(typeof(Greetings).FullName!));
        Assert.Null(catalog.Find(typeof(CatalogModel.Nested).FullName!));
        Assert.Null(catalog.Find(typeof(CatalogMarkAttribute).FullName!));
        Assert.Null(catalog.Find(typeof(CatalogBox<>).FullName!));
        Assert.Null(catalog.Find(typeof(CatalogCallback).FullName!));

        Assert.All(catalog.Entries, static entry =>
        {
            Assert.DoesNotContain('`', entry.Name);
            Assert.DoesNotContain('!', entry.FullName);
        });

        // The order a list shows them in.
        Assert.Equal(
            catalog.Entries.Select(static entry => entry.Name).Order(StringComparer.Ordinal),
            catalog.Entries.Select(static entry => entry.Name));
    }

    [Fact]
    public void ACatalogKeepsNothingOfTheAssembliesItWasReadFrom()
    {
        (WeakReference context, XamlTypeCatalog catalog) = ReadFromAnUnloadableCopy();

        for (int pass = 0; pass < 10 && context.IsAlive; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // The catalog is alive and still answers; the generation it was read from is gone.
        Assert.False(context.IsAlive);
        Assert.Contains(catalog.Entries, static entry => entry.Name == nameof(CountedView));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Context, XamlTypeCatalog Catalog) ReadFromAnUnloadableCopy()
    {
        var context = new AssemblyLoadContext("catalog-copy", isCollectible: true);
        Assembly copy = context.LoadFromAssemblyPath(typeof(CountedView).Assembly.Location);

        XamlTypeCatalog catalog = XamlTypeCatalog.Create([copy]);

        context.Unload();

        return (new WeakReference(context), catalog);
    }
}

/// <summary>A model in an assembly that maps no XAML namespace.</summary>
public abstract class CatalogModel
{
    /// <summary>A type nested in another, which a document cannot name.</summary>
    public sealed class Nested;
}

/// <summary>An attribute, which a document does not write as an element.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CatalogMarkAttribute : Attribute;

/// <summary>A generic definition, which a document cannot name without its arguments.</summary>
/// <typeparam name="T">Anything.</typeparam>
public class CatalogBox<T>;

/// <summary>A delegate, which a document does not write as an element.</summary>
public delegate void CatalogCallback();
