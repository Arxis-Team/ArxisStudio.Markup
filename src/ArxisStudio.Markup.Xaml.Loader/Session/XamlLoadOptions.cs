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
    /// This is the assembly a document's own <c>using:</c> namespaces belong to — the project
    /// the file lives in. Without it, Avalonia still resolves anything the process has loaded.
    /// </remarks>
    public Assembly? LocalAssembly { get; init; }

    /// <summary>Gets a value indicating whether bindings are compiled unless a document says otherwise.</summary>
    public bool UseCompiledBindingsByDefault { get; init; }
}
