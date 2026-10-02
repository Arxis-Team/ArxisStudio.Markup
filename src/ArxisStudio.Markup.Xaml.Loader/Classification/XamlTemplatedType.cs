using System;
using System.Diagnostics.CodeAnalysis;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// A control whose <c>Template</c> a document sets, and the declaration that gives it the
/// template.
/// </summary>
public sealed class XamlTemplatedType
{
    internal XamlTemplatedType(XamlTypeReference reference, Type? type, XamlStyleDeclaration declaration)
    {
        Reference = reference;
        Type = type;
        Declaration = declaration;
    }

    /// <summary>Gets where the document names the control.</summary>
    public XamlTypeReference Reference { get; }

    /// <summary>
    /// Gets the control's type, or <see langword="null"/> when the environment could not resolve
    /// it and the document was judged by its names.
    /// </summary>
    public Type? Type { get; }

    /// <summary>
    /// Gets the style or control theme written for the control — the one that sets its template,
    /// or the one based on a theme in the same document that does.
    /// </summary>
    public XamlStyleDeclaration Declaration { get; }

    /// <summary>Gets a value indicating whether the control's type was resolved.</summary>
    [MemberNotNullWhen(true, nameof(Type))]
    public bool IsResolved => Type is not null;

    /// <summary>Returns the control's type, or its name when the type was not resolved.</summary>
    /// <returns>A readable description of the control.</returns>
    public override string ToString() => Type?.FullName ?? Reference.ToString();
}
