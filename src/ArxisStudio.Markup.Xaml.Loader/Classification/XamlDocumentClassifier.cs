using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Says what a document is — a window, a user control, a templated control's look, a set of
/// styles — by its root's type, and by its styles where the root does not say.
/// </summary>
/// <remarks>
/// <para>
/// The root element's type decides first, through the environment's type resolver: an
/// <c>Application</c>, a <c>Window</c>, a <c>UserControl</c>, any other <c>Control</c>, a set of
/// styles, a resource dictionary, in that order, each including what derives from it. The
/// <c>x:Class</c> is not consulted — it derives from the root element's type and cannot change
/// which of these it is.
/// </para>
/// <para>
/// A set of styles and a resource dictionary are also what a templated control's look is written
/// in, so for those two the declarations are read as well (<see cref="XamlStyleAnalyzer"/>). A
/// style or control theme that sets <c>Template</c> on a control written outside Avalonia's own
/// namespace — itself, or through a control theme in the same document it is <c>BasedOn</c> —
/// makes the document a <see cref="XamlDocumentKind.TemplatedControl"/>; setting it on
/// Avalonia's own <c>Button</c> is a theme, and leaves it what its root says. "Outside" is a
/// question about the namespace the name is written in, not about where the type lives, so the
/// answer is the same whether or not the environment can resolve the control: a project's look
/// files classify before the project is built. When the control does resolve, it has to be a
/// <see cref="TemplatedControl"/>.
/// </para>
/// <para>
/// Nothing is created and nothing is loaded: the answer is metadata, resolved through the same
/// cached resolver a session uses, so classifying a whole folder costs one lookup per distinct
/// type. It touches no Avalonia object and runs on any thread.
/// </para>
/// </remarks>
public static class XamlDocumentClassifier
{
    /// <summary>The namespace Avalonia's own types are written in.</summary>
    public const string AvaloniaNamespace = "https://github.com/avaloniaui";

    private const string TemplateMember = "Template";
    private const string BasedOnAttribute = "BasedOn";

    /// <summary>Classifies a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="environment">Where types are resolved — the one a session would load it in.</param>
    /// <param name="cancellationToken">Cancels the classification.</param>
    /// <returns>What the document is, and what the answer rests on.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="document"/> or <paramref name="environment"/> is <see langword="null"/>.
    /// </exception>
    public static async ValueTask<XamlDocumentClassification> ClassifyAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(environment);

        cancellationToken.ThrowIfCancellationRequested();

        // No root, or a property element in its place, is already a syntax diagnostic of the
        // document's own; there is nothing further to say about it here.
        if (document.Root is not { IsPropertyElementSyntax: false } root)
        {
            return new XamlDocumentClassification(
                XamlDocumentKind.Unknown, document.Root, null, false, [], null, false, []);
        }

        var diagnostics = new List<MarkupDiagnostic>();
        bool isCustomRoot = root.NamespaceUri is { } rootNamespace && !IsAvalonia(rootNamespace);
        XamlElement? preview = PreviewOf(root);

        Type? rootType = await ResolveAsync(
                document,
                root.Name,
                root.NamespaceUri,
                root.NamespaceContext,
                root.NameSpan,
                MarkupDiagnosticSeverity.Error,
                environment,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        // A root nobody can resolve is not guessed at. Its name says nothing reliable about what
        // it derives from, and a document whose root cannot be resolved cannot be loaded either.
        if (rootType is null)
        {
            return new XamlDocumentClassification(
                XamlDocumentKind.Unknown, root, null, isCustomRoot, [], preview, false, [.. diagnostics]);
        }

        XamlDocumentKind kind = KindOf(rootType);

        if (kind is not (XamlDocumentKind.Styles or XamlDocumentKind.ResourceDictionary))
        {
            return new XamlDocumentClassification(
                kind, root, rootType, isCustomRoot, [], preview, true, [.. diagnostics]);
        }

        (ImmutableArray<XamlTemplatedType> templated, bool resolved) =
            await TemplatedTypesAsync(document, environment, diagnostics, cancellationToken).ConfigureAwait(false);

        return new XamlDocumentClassification(
            templated.IsEmpty ? kind : XamlDocumentKind.TemplatedControl,
            root,
            rootType,
            isCustomRoot,
            templated,
            preview,
            resolved,
            [.. diagnostics]);
    }

    private static XamlDocumentKind KindOf(Type type) =>
        typeof(Application).IsAssignableFrom(type) ? XamlDocumentKind.Application
        : typeof(Window).IsAssignableFrom(type) ? XamlDocumentKind.Window
        : typeof(UserControl).IsAssignableFrom(type) ? XamlDocumentKind.UserControl
        : typeof(Control).IsAssignableFrom(type) ? XamlDocumentKind.Control
        : typeof(IStyle).IsAssignableFrom(type) ? XamlDocumentKind.Styles
        : typeof(IResourceDictionary).IsAssignableFrom(type) ? XamlDocumentKind.ResourceDictionary
        : XamlDocumentKind.Other;

    /// <summary>
    /// Finds the controls outside Avalonia's namespace whose template the document sets.
    /// </summary>
    /// <returns>The controls, each once, and whether every one of them resolved.</returns>
    private static async ValueTask<(ImmutableArray<XamlTemplatedType> Types, bool Resolved)> TemplatedTypesAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ImmutableArray<XamlTemplatedType>.Builder found = ImmutableArray.CreateBuilder<XamlTemplatedType>();
        var seen = new HashSet<(string? NamespaceUri, string Name)>();
        bool resolved = true;

        ImmutableArray<XamlStyleDeclaration> declarations = XamlStyleAnalyzer.Discover(document);
        Dictionary<ThemeKey, XamlStyleDeclaration> themes = ThemesByKey(declarations);

        foreach (XamlStyleDeclaration declaration in declarations)
        {
            if (IsInPreview(declaration.Element) || !SetsTemplate(declaration, themes))
            {
                continue;
            }

            foreach (XamlTypeReference target in declaration.Targets)
            {
                if (target.NamespaceUri is { } namespaceUri && IsAvalonia(namespaceUri))
                {
                    continue;
                }

                // Asked once per control, so a control themed by several declarations is listed —
                // and, when it cannot be resolved, reported — once.
                if (!seen.Add((target.NamespaceUri, target.NamespaceUri is null ? target.Name.ToString() : target.Name.LocalName)))
                {
                    continue;
                }

                Type? type = await ResolveAsync(
                        document,
                        target.Name,
                        target.NamespaceUri,
                        declaration.Element.NamespaceContext,
                        target.Span,
                        MarkupDiagnosticSeverity.Warning,
                        environment,
                        diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);

                // A prefix nothing declares names no namespace at all, so not one outside
                // Avalonia's either. It is reported and passed over.
                if (target.NamespaceUri is null)
                {
                    continue;
                }

                if (type is null)
                {
                    found.Add(new XamlTemplatedType(target, null, declaration));
                    resolved = false;
                }
                else if (typeof(TemplatedControl).IsAssignableFrom(type))
                {
                    found.Add(new XamlTemplatedType(target, type, declaration));
                }
            }
        }

        return (found.ToImmutable(), resolved);
    }

    /// <summary>
    /// Decides whether a declaration gives its targets a template: with a setter of its own, or
    /// through the control theme in this document it is <c>BasedOn</c>.
    /// </summary>
    /// <remarks>
    /// A theme library writes one base theme with the template and a theme per control that is
    /// based on it and sets nothing of the kind itself — so without following the chain, the file
    /// that is the whole look of <c>AxButton</c> would read as a dictionary. Only this document is
    /// followed: a base theme from elsewhere supplies a template this file does not.
    /// </remarks>
    private static bool SetsTemplate(
        XamlStyleDeclaration declaration,
        Dictionary<ThemeKey, XamlStyleDeclaration> themes)
    {
        var visited = new HashSet<XamlStyleDeclaration>();

        for (XamlStyleDeclaration? current = declaration;
             current is not null && visited.Add(current);
             current = BasedOnOf(current, themes))
        {
            if (current.Setters.Any(IsTemplateSetter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the control theme in this document another is based on, by its key.</summary>
    private static XamlStyleDeclaration? BasedOnOf(
        XamlStyleDeclaration declaration,
        Dictionary<ThemeKey, XamlStyleDeclaration> themes) =>
        declaration.Kind == XamlStyleKind.ControlTheme
        && declaration.Element.GetAttribute(BasedOnAttribute)?.GetValue() is XamlMarkupExtensionValue
        {
            TypeName.LocalName: "StaticResource",
        } reference
        && (reference.PositionalArguments.FirstOrDefault() ?? reference.GetArgument("ResourceKey"))?.Value is { } key
        && KeyOf(key, declaration.Element) is { } themeKey
        && themes.TryGetValue(themeKey, out XamlStyleDeclaration? based)
            ? based
            : null;

    /// <summary>Indexes the document's control themes by their <c>x:Key</c>.</summary>
    /// <remarks>The first theme under a key wins, as the dictionary they are added to would have it.</remarks>
    private static Dictionary<ThemeKey, XamlStyleDeclaration> ThemesByKey(
        ImmutableArray<XamlStyleDeclaration> declarations)
    {
        var themes = new Dictionary<ThemeKey, XamlStyleDeclaration>();

        foreach (XamlStyleDeclaration declaration in declarations)
        {
            if (declaration.Kind == XamlStyleKind.ControlTheme
                && declaration.Element.GetDirectiveAttribute(XamlDirectives.Key)?.GetValue() is { } key
                && KeyOf(key, declaration.Element) is { } themeKey)
            {
                themes.TryAdd(themeKey, declaration);
            }
        }

        return themes;
    }

    /// <summary>
    /// Reads a resource key the way <c>x:Key</c> and <c>StaticResource</c> both write one: as
    /// text, or as <c>{x:Type name}</c>, which is compared by the type it names rather than by
    /// how its prefix is spelled.
    /// </summary>
    private static ThemeKey? KeyOf(XamlValue value, XamlElement element)
    {
        switch (value)
        {
            case XamlLiteralValue literal:
                return new ThemeKey(null, literal.Text.Trim());

            case XamlMarkupExtensionValue { TypeName.LocalName: "Type" or "TypeExtension" } extension
                when string.Equals(
                    element.NamespaceContext.LookupNamespace(extension.TypeName.Prefix),
                    XamlNamespaces.Xaml,
                    StringComparison.Ordinal)
                && (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("TypeName"))
                    ?.Value is XamlLiteralValue type:
                var name = XamlQualifiedName.Parse(type.Text.Trim());

                return element.NamespaceContext.LookupNamespace(name.Prefix) is { } namespaceUri
                    ? new ThemeKey(namespaceUri, name.LocalName)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Decides whether a setter sets <c>Template</c>, in any of the ways a setter can name it:
    /// <c>Template</c>, <c>TemplatedControl.Template</c>, <c>(TemplatedControl.Template)</c>.
    /// </summary>
    private static bool IsTemplateSetter(XamlStyleSetter setter)
    {
        if (setter.Property is not { } property)
        {
            return false;
        }

        ReadOnlySpan<char> name = property.AsSpan().Trim();

        if (name.Length >= 2 && name[0] == '(' && name[^1] == ')')
        {
            name = name[1..^1].Trim();
        }

        return name[(name.LastIndexOf('.') + 1)..].Equals(TemplateMember, StringComparison.Ordinal);
    }

    private static XamlElement? PreviewOf(XamlElement root) =>
        root.MemberElements.FirstOrDefault(IsPreviewMember)?.ContentElements.FirstOrDefault();

    private static bool IsInPreview(XamlElement element) =>
        element.AncestorsAndSelf().OfType<XamlElement>().Any(IsPreviewMember);

    private static bool IsPreviewMember(XamlElement element) =>
        element.IsPropertyElementSyntax
        && string.Equals(element.OwnerName, "Design", StringComparison.Ordinal)
        && string.Equals(element.MemberName, "PreviewWith", StringComparison.Ordinal);

    private static bool IsAvalonia(string namespaceUri) =>
        string.Equals(namespaceUri, AvaloniaNamespace, StringComparison.Ordinal);

    /// <summary>
    /// A control theme's key: text, or the type an <c>{x:Type}</c> names — which carries its
    /// namespace, so text and type never collide.
    /// </summary>
    private readonly record struct ThemeKey(string? NamespaceUri, string Name);

    /// <summary>
    /// Resolves a name written in the document, reporting a failure where the name is written.
    /// </summary>
    /// <remarks>
    /// The resolver's own diagnostics carry no location — it is handed a name, not a document — so
    /// they are given this document and this span, and the severity the caller's answer warrants.
    /// </remarks>
    private static async ValueTask<Type?> ResolveAsync(
        XamlDocument document,
        XamlQualifiedName name,
        string? namespaceUri,
        XamlNamespaceContext context,
        TextSpan span,
        MarkupDiagnosticSeverity severity,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (namespaceUri is null)
        {
            diagnostics.Add(name.Prefix is { } prefix
                ? MarkupDiagnostic.Resolution(
                    XamlLoaderDiagnosticCodes.UndeclaredPrefix,
                    $"The prefix '{prefix}' on '{name}' is not declared anywhere in scope. "
                        + $"Declare it with an xmlns:{prefix} attribute.",
                    severity,
                    document.Uri,
                    span)
                : MarkupDiagnostic.Resolution(
                    XamlLoaderDiagnosticCodes.UnresolvedType,
                    $"'{name}' is in no namespace: nothing in scope declares a default xmlns.",
                    severity,
                    document.Uri,
                    span));

            return null;
        }

        XamlTypeResolution resolution = await environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, name.LocalName), context, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Success)
        {
            return resolution.Type;
        }

        diagnostics.AddRange(resolution.Diagnostics.Select(diagnostic =>
            diagnostic with { Severity = severity, DocumentUri = document.Uri, Span = span }));

        return null;
    }
}
