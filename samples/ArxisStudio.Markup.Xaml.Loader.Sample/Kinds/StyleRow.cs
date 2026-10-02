using System;
using System.Linq;
using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Kinds;

/// <summary>
/// One style declaration of the document, as the syntax package reads it.
/// </summary>
/// <remarks>
/// Every word on the row comes from <see cref="XamlStyleDeclaration"/>, which knows nothing of
/// Avalonia: the targets are names with the namespace their prefix means, and the setters are
/// properties as written. Selecting the row selects the first target in the text, where its
/// <see cref="XamlTypeReference.Span"/> says it is — through entity references and all.
/// </remarks>
internal sealed class StyleRow
{
    private const double IndentStep = 14;

    internal StyleRow(XamlStyleDeclaration declaration)
    {
        Kind = declaration.Kind.ToString();

        Head = declaration.Kind == XamlStyleKind.ControlTheme
            ? Describe(declaration.Element)
            : declaration.Selector ?? "без селектора";

        Targets = declaration.Targets.IsEmpty
            ? "цели нет: селектор не называет тип"
            : "→ " + string.Join(", ", declaration.Targets.Select(static target => target.Name.LocalName));

        Namespaces = string.Join(
            ", ",
            declaration.Targets
                .Select(static target => target.NamespaceUri ?? "префикс не объявлен")
                .Distinct(StringComparer.Ordinal));

        Setters = declaration.Setters.IsEmpty
            ? "сеттеров нет"
            : "задаёт " + string.Join(", ", declaration.Setters.Select(static setter => setter.Property ?? "?"));

        SetsTemplate = declaration.Setters.Any(static setter =>
            setter.Property?.Trim().Trim('(', ')').Split('.')[^1] == "Template");

        int depth = 0;

        for (XamlStyleDeclaration? parent = declaration.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        Indent = new Thickness(depth * IndentStep, 0, 0, 0);
        Span = declaration.Targets.IsEmpty ? declaration.Element.NameSpan : declaration.Targets[0].Span;
    }

    /// <summary>Gets <c>Style</c> or <c>ControlTheme</c>.</summary>
    public string Kind { get; }

    /// <summary>Gets the selector, or what a control theme says about its target.</summary>
    public string Head { get; }

    /// <summary>Gets the types the setters land on.</summary>
    public string Targets { get; }

    /// <summary>Gets the namespaces those types are written in, as their prefixes resolve.</summary>
    public string Namespaces { get; }

    /// <summary>Gets the properties the declaration sets itself.</summary>
    public string Setters { get; }

    /// <summary>Gets a value indicating whether one of them is the template.</summary>
    public bool SetsTemplate { get; }

    /// <summary>Gets the room a nested declaration's depth earns it.</summary>
    public Thickness Indent { get; }

    /// <summary>Gets the range to select in the text.</summary>
    internal TextSpan Span { get; }

    private static string Describe(XamlElement theme)
    {
        string target = theme.GetAttribute("TargetType")?.GetValueText() ?? "?";

        return theme.GetAttribute("BasedOn") is { } basedOn
            ? $"TargetType={target}, BasedOn={basedOn.GetValueText()}"
            : $"TargetType={target}";
    }
}
