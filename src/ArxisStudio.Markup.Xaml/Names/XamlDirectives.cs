namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The local names of the XAML-language directives, which live in
/// <see cref="XamlNamespaces.Xaml"/>.
/// </summary>
/// <remarks>
/// Recognising these is a convenience for callers, not a filter. A directive this list has
/// never heard of is still a directive, still resolvable through the namespace, and still
/// written back exactly as it was — the parser has no opinion about which ones exist.
/// </remarks>
public static class XamlDirectives
{
    /// <summary>The CLR type the document's root is a partial definition of.</summary>
    public const string Class = "Class";

    /// <summary>The name an object is registered under in its name scope.</summary>
    public const string Name = "Name";

    /// <summary>The key an object is stored under in a resource dictionary.</summary>
    public const string Key = "Key";

    /// <summary>The type compiled bindings in this scope are written against.</summary>
    public const string DataType = "DataType";

    /// <summary>The type arguments of a generic type.</summary>
    public const string TypeArguments = "TypeArguments";

    /// <summary>Whether bindings in this scope are compiled.</summary>
    public const string CompileBindings = "CompileBindings";

    /// <summary>The base class of the generated partial class.</summary>
    public const string ClassModifier = "ClassModifier";

    /// <summary>The declared accessibility of a generated field.</summary>
    public const string FieldModifier = "FieldModifier";

    /// <summary>A shared-instance marker in a resource dictionary.</summary>
    public const string Shared = "Shared";

    /// <summary>The <c>{x:Null}</c> extension's type name.</summary>
    public const string Null = "Null";

    /// <summary>The <c>{x:Static}</c> extension's type name.</summary>
    public const string Static = "Static";

    /// <summary>The <c>{x:Type}</c> extension's type name.</summary>
    public const string Type = "Type";
}
