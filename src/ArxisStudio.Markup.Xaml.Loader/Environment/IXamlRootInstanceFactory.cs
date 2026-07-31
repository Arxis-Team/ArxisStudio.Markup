using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Creates the object a document with <c>x:Class</c> populates.
/// </summary>
/// <remarks>
/// <para>
/// A class whose constructor calls <c>InitializeComponent()</c> will load the document a second
/// time if it is constructed normally and then populated. A factory exists so the caller can
/// decide how to avoid that — a parameterless constructor guarded by a flag, a purpose-built
/// constructor, or something else it knows about its own types.
/// </para>
/// <para>
/// The default deliberately does not reach for uninitialised-object creation. Skipping
/// constructors leaves fields unset in ways that surface far from the cause.
/// </para>
/// </remarks>
public interface IXamlRootInstanceFactory
{
    /// <summary>Creates the root instance for a document.</summary>
    /// <param name="rootType">The type named by the document's <c>x:Class</c>.</param>
    /// <param name="context">What is known about the document being loaded.</param>
    /// <param name="cancellationToken">A token to observe while creating.</param>
    /// <returns>The instance the document will populate.</returns>
    ValueTask<object> CreateAsync(Type rootType, XamlRootInstanceContext context, CancellationToken cancellationToken);
}

/// <summary>What a root-instance factory is told about the document being loaded.</summary>
/// <param name="Document">The document the instance is being created for.</param>
/// <param name="Mode">Whether the document is being loaded for design or for real.</param>
public readonly record struct XamlRootInstanceContext(XamlDocument Document, XamlLoadMode Mode);
