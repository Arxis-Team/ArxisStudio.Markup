using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The design-time half of a load: which attributes Avalonia's loader must not see, and what to
/// do with them once the objects exist.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia records <c>d:DesignWidth</c>, <c>d:DesignHeight</c> and <c>d:DataContext</c> on the
/// <see cref="Design"/> attached properties and stops there — nothing applies them to the control,
/// because in Avalonia's own use a designer host reads them back and decides. The contract asks
/// for them to be honoured, so this applies them, and it does so by reading the attached
/// properties rather than the document: that way <c>&lt;Design.DataContext&gt;</c> written as an
/// element, and a value written as a markup extension, both arrive already evaluated.
/// </para>
/// <para>
/// Every other name in the design namespace has no emitter at all, so one <c>d:Text</c> fails the
/// whole document. Those are the shadow values: removed from the projection so the load survives,
/// then applied here as ordinary property sets on the object the element produced.
/// </para>
/// <para>
/// In run mode they are removed and not applied, which is what ignoring a design-only value
/// means. The document is untouched either way and still writes back exactly as written.
/// </para>
/// </remarks>
internal static class XamlDesignValues
{
    /// <summary>
    /// The names Avalonia's own design transformer understands. Everything else in the design
    /// namespace reaches an emitter that does not exist.
    /// </summary>
    private static readonly string[] Understood =
        ["DesignWidth", "DesignHeight", "DataContext", "PreviewWith"];

    /// <summary>
    /// Reports whether Avalonia's loader has to be shown the document without this attribute.
    /// </summary>
    /// <remarks>
    /// Two reasons, one answer. A design-namespace attribute it has no emitter for would fail the
    /// document; an attribute whose prefix <c>mc:Ignorable</c> lists is, by definition, content a
    /// reader that does not understand it should carry on without.
    /// </remarks>
    /// <param name="attribute">The attribute to judge.</param>
    /// <param name="element">The element it is written on, for the declarations in scope there.</param>
    /// <returns><see langword="true"/> if the projection must leave it out.</returns>
    internal static bool IsHiddenFromLoader(XamlAttribute attribute, XamlElement element)
    {
        // Namespace declarations are what the rest is resolved against, and mc: attributes are
        // the instructions themselves. Removing either changes what the document means.
        if (attribute is XamlNamespaceDeclaration || attribute.IsMarkupCompatibility)
        {
            return false;
        }

        if (attribute.IsDesignTime)
        {
            return !IsUnderstood(attribute.Name.LocalName);
        }

        // An unprefixed attribute is in no namespace at all — XML does not apply a default
        // namespace to attributes — so it is a member of the element and never ignorable.
        return attribute.NamespaceUri is { } namespaceUri
            && IgnorableNamespaces(element).Contains(namespaceUri, StringComparer.Ordinal);
    }

    /// <summary>Applies a design-mode document's design-time values to the objects it produced.</summary>
    /// <param name="document">The document that was loaded.</param>
    /// <param name="objects">Which element each object came from.</param>
    /// <param name="root">The object the document produced.</param>
    /// <param name="members">What decides which member a design-time name shadows.</param>
    /// <param name="diagnostics">Collects everything noticed on the way.</param>
    internal static void Apply(
        XamlDocument document,
        XamlObjectMap objects,
        object root,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        ApplyDesignSurface(root);

        foreach (XamlElement element in document.DescendantElements())
        {
            bool isRoot = ReferenceEquals(element, document.Root);

            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (!attribute.IsDesignTime)
                {
                    continue;
                }

                if (!IsUnderstood(attribute.Name.LocalName))
                {
                    ApplyShadowValue(document, objects, element, attribute, members, diagnostics);
                }
                else if (isRoot)
                {
                    // Avalonia evaluated these into the Design attached properties, which is
                    // what ApplyDesignSurface just read. Reading the document as well is what
                    // makes a later update to one of them land: nothing re-evaluates on an
                    // update, so the attached property still holds what the load put there.
                    ApplySurfaceValue(root, attribute);
                }

                // Avalonia ignores these names anywhere but the root, and so does this.
            }
        }
    }

    /// <summary>
    /// Applies the size and data context a design surface is meant to show the document at.
    /// </summary>
    private static void ApplyDesignSurface(object root)
    {
        if (root is not Control control)
        {
            return;
        }

        // Whether the value was written is the question, not whether it happens to differ from
        // the default: d:DesignWidth="0" says something, and so does its absence.
        if (control.IsSet(Design.WidthProperty))
        {
            control.Width = Design.GetWidth(control);
        }

        if (control.IsSet(Design.HeightProperty))
        {
            control.Height = Design.GetHeight(control);
        }

        if (control.IsSet(Design.DataContextProperty))
        {
            control.DataContext = Design.GetDataContext(control);
        }
    }

    /// <summary>Applies one of the surface values the document states as a literal.</summary>
    private static void ApplySurfaceValue(object root, XamlAttribute attribute)
    {
        if (root is not Control control || attribute.GetValue() is not XamlLiteralValue literal)
        {
            return;
        }

        switch (attribute.Name.LocalName)
        {
            case "DesignWidth" when double.TryParse(literal.Text, CultureInfo.InvariantCulture, out double width):
                control.Width = width;

                break;

            case "DesignHeight" when double.TryParse(literal.Text, CultureInfo.InvariantCulture, out double height):
                control.Height = height;

                break;

            case "DataContext":
                control.DataContext = literal.Text;

                break;

            default:
                break;
        }
    }

    /// <summary>Applies one shadow value, such as <c>d:Text</c>, to the object its element produced.</summary>
    private static void ApplyShadowValue(
        XamlDocument document,
        XamlObjectMap objects,
        XamlElement element,
        XamlAttribute attribute,
        XamlMemberResolver members,
        List<MarkupDiagnostic> diagnostics)
    {
        string name = attribute.Name.LocalName;

        if (objects.GetObject(element) is not { } target)
        {
            // No object came of the element — it is inside a template, or the load stopped
            // short of it. There is nothing to put the value on, and saying so beats silence.
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.DesignValueNotApplied,
                $"'{attribute.Name}' has no object to apply to: <{element.Name}> produced none.",
                MarkupDiagnosticSeverity.Info,
                document.Uri,
                attribute.Span));

            return;
        }

        if (attribute.GetValue() is not XamlLiteralValue literal)
        {
            // Evaluating an expression is what a load does, and this value was kept out of the
            // one that just ran. Reporting it beats writing the expression's text as a literal.
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.DesignValueNotApplied,
                $"'{attribute.Name}' is written as an expression, which a design-time value cannot be.",
                MarkupDiagnosticSeverity.Info,
                document.Uri,
                attribute.Span));

            return;
        }

        XamlMemberDescriptor member = members.Resolve(target.GetType(), name);

        if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.UnresolvedDesignMember,
                member.IsResolved
                    ? $"'{attribute.Name}' names {target.GetType().Name}.{name}, which cannot be written."
                    : $"'{attribute.Name}' names no member of {target.GetType().Name}.",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                attribute.Span));

            return;
        }

        XamlValueConversionResult value = member.ConvertFromText(literal.Text);

        if (!value.Succeeded)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.TypeConverterFailure,
                $"'{attribute.Name}' could not be applied: {value.Error}",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                attribute.Span));

            return;
        }

        try
        {
            Write(target, member, value.Value);
        }
        catch (Exception error) when (error is InvalidCastException or ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.IncompatibleValue,
                $"'{literal.Text}' could not be assigned to {target.GetType().Name}.{name}: {error.Message}",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                attribute.Span));
        }
    }

    /// <summary>Writes a value through whichever accessor the member actually has.</summary>
    internal static void Write(object target, XamlMemberDescriptor member, object? value)
    {
        if (member.AvaloniaProperty is { } property && target is AvaloniaObject avaloniaObject)
        {
            avaloniaObject.SetValue(property, value);

            return;
        }

        if (member is { IsAttached: true, AttachedAccessors.Setter: { } setter })
        {
            setter.Invoke(null, [target, value]);

            return;
        }

        member.ClrProperty?.SetValue(target, value);
    }

    private static bool IsUnderstood(string localName) =>
        Understood.Contains(localName, StringComparer.Ordinal);

    /// <summary>
    /// Gets the namespaces <c>mc:Ignorable</c> declares ignorable at an element.
    /// </summary>
    /// <remarks>
    /// The attribute lists prefixes, and prefixes are resolved where they are written, so each is
    /// turned into the URI it names there. Comparing prefixes instead would mistake two documents
    /// that spell the same namespace differently for different documents.
    /// </remarks>
    private static IEnumerable<string> IgnorableNamespaces(XamlElement element)
    {
        foreach (XamlElement scope in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            XamlAttribute? ignorable = scope.Attributes.FirstOrDefault(
                static attribute => attribute.IsMarkupCompatibility
                    && string.Equals(attribute.Name.LocalName, "Ignorable", StringComparison.Ordinal));

            if (ignorable is null)
            {
                continue;
            }

            foreach (string prefix in ignorable.GetValueText()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (element.NamespaceContext.LookupNamespace(prefix) is { } namespaceUri)
                {
                    yield return namespaceUri;
                }
            }
        }
    }
}
