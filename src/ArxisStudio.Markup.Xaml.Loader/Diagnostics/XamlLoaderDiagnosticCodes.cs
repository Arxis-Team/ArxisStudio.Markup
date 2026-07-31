namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The diagnostic codes this package produces.
/// </summary>
/// <remarks>
/// <c>AXM2xxx</c> is reserved for resolution and <c>AXM3xxx</c> for loading. The syntax
/// package owns <c>AXM1xxx</c>; a code's meaning never changes and a retired code is never
/// reused, because consumers suppress and route on them.
/// </remarks>
public static class XamlLoaderDiagnosticCodes
{
    /// <summary>An assembly named by a XAML namespace could not be resolved.</summary>
    public const string UnresolvedAssembly = "AXM2001";

    /// <summary>A type named in the document could not be resolved.</summary>
    public const string UnresolvedType = "AXM2002";

    /// <summary>An element's namespace prefix is not declared anywhere in scope.</summary>
    public const string UndeclaredPrefix = "AXM2003";

    /// <summary>A XAML namespace URI matched no assembly, namespace or declaration.</summary>
    public const string UnknownNamespace = "AXM2004";

    /// <summary>A resource URI could not be resolved to any content.</summary>
    public const string UnresolvedResource = "AXM2005";

    /// <summary>A generic type was named without the arguments it needs.</summary>
    public const string UnsupportedGenericType = "AXM2006";

    /// <summary>Avalonia reported a problem while loading the document.</summary>
    public const string RuntimeLoadFailure = "AXM3001";

    /// <summary>Creating an object from the document threw.</summary>
    public const string ObjectCreationFailure = "AXM3002";

    /// <summary>The document produced no root object.</summary>
    public const string NoRootObject = "AXM3003";

    /// <summary>An Avalonia object was touched from a thread that does not own it.</summary>
    public const string InvalidThreadAccess = "AXM3004";
}
