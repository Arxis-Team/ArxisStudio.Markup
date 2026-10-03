using System.Reflection;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>How a document should be loaded.</summary>
public sealed class XamlLoadOptions
{
    /// <summary>Gets the options used when none are supplied.</summary>
    public static XamlLoadOptions Default { get; } = new();

    /// <summary>Gets whether the document is being loaded to run or to be looked at.</summary>
    public XamlLoadMode Mode { get; init; } = XamlLoadMode.Runtime;

    /// <summary>
    /// Gets the assembly unqualified type names are resolved against, if the caller knows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assembly a document's own <c>using:</c> namespaces belong to — the project
    /// the file lives in — and the one whose non-public members the document may name: a runtime
    /// load compiles the text into an assembly of its own, which calls a private handler of the
    /// class only when the load names the class's assembly.
    /// </para>
    /// <para>
    /// Without it, a session takes the assembly of the class the document's <c>x:Class</c> names,
    /// when that class resolves; and with no class either, Avalonia resolves anything the process
    /// has loaded.
    /// </para>
    /// </remarks>
    public Assembly? LocalAssembly { get; init; }

    /// <summary>Gets a value indicating whether bindings are compiled unless a document says otherwise.</summary>
    public bool UseCompiledBindingsByDefault { get; init; }

    /// <summary>Gets what is done with the class the document's <c>x:Class</c> names.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="XamlClassUse.Construct"/> by default: the class is constructed and populated from
    /// the document, as the program would build it.
    /// </para>
    /// <para>
    /// <see cref="XamlClassUse.AsWritten"/> leaves the class out the way a class the load cannot use
    /// is left out (ADR 0017): the directive is kept out of the text Avalonia is given, by every
    /// projection the session makes, and the root is built as the element it is written as. It is
    /// for a host that wants what a document declares and not what its class does — an application's
    /// <c>App.axaml</c>, whose class constructed inside a designer would be the program starting up
    /// there (ADR 0027).
    /// </para>
    /// </remarks>
    public XamlClassUse ClassUse { get; init; } = XamlClassUse.Construct;

    /// <summary>
    /// Gets what gives the session the whole of its root back, when a host has borrowed parts of it.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when nothing borrows from the root, which is a host that shows the root
    /// as it is. One that shows a window does not: a window cannot be the content of anything, so
    /// its content and resources are taken out of it and shown elsewhere — and an update writing
    /// into that root, or rebuilding the map by walking it, would meet a window with nothing in it.
    /// See <see cref="IXamlRootAccess"/>.
    /// </remarks>
    public IXamlRootAccess? RootAccess { get; init; }
}
