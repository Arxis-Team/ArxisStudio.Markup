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

    /// <summary>A member named in an edit is not a member of the target type.</summary>
    public const string UnresolvedMember = "AXM3010";

    /// <summary>An edit targeted a member that cannot be written.</summary>
    public const string ReadOnlyMember = "AXM3011";

    /// <summary>A value could not be assigned to the member it was meant for.</summary>
    public const string IncompatibleValue = "AXM3012";

    /// <summary>A type converter refused a value.</summary>
    public const string TypeConverterFailure = "AXM3013";

    /// <summary>An edit replaced a binding or resource reference with a literal.</summary>
    public const string ExpressionReplaced = "AXM3014";

    /// <summary>An expression was written to the document but not evaluated onto the object.</summary>
    public const string ExpressionNotApplied = "AXM3015";

    /// <summary>An edit targeted an object nothing in this document declares.</summary>
    public const string NoSourceDeclaration = "AXM3016";

    /// <summary>The type named by <c>x:Class</c> could not be resolved.</summary>
    public const string UnresolvedRootType = "AXM3020";

    /// <summary>The type named by <c>x:Class</c> is not compatible with the root element.</summary>
    public const string IncompatibleRootType = "AXM3021";

    /// <summary>Creating the root instance threw.</summary>
    public const string RootInstanceCreationFailure = "AXM3022";

    /// <summary>A root-instance factory returned something the document cannot populate.</summary>
    public const string InvalidRootFactoryResult = "AXM3023";
}
