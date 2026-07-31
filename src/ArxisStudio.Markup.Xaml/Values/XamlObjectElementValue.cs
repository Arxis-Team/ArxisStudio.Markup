using System;
using System.Linq;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A value written as nested elements rather than as attribute text.
/// </summary>
/// <remarks>
/// This is what <c>&lt;Button.Background&gt;&lt;SolidColorBrush /&gt;&lt;/Button.Background&gt;</c>
/// sets. The elements are kept as they are: this package knows the shape of the syntax and
/// nothing about what the objects would be.
/// </remarks>
/// <param name="Elements">The element or elements the member was set to, in source order.</param>
public sealed record XamlObjectElementValue(ImmutableArray<XamlElement> Elements) : XamlValue
{
    /// <summary>Gets the elements the member was set to.</summary>
    public ImmutableArray<XamlElement> Elements { get; } = Elements.IsDefault ? [] : Elements;

    /// <summary>Gets a value indicating whether the member was set to more than one element.</summary>
    public bool IsCollection => Elements.Length > 1;

    /// <summary>
    /// Renders the elements as XAML.
    /// </summary>
    /// <remarks>
    /// An object-element value cannot be written between an attribute's quotes; this is its
    /// element text, which callers place in content position.
    /// </remarks>
    /// <returns>The elements' source text, concatenated.</returns>
    public override string ToXamlText() =>
        string.Concat(Elements.Select(static element => element.GetText()));
}
