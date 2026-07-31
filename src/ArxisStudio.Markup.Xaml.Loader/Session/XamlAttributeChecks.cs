using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Checks the attributes a load is about to act on, before it acts on them.
/// </summary>
/// <remarks>
/// <para>
/// Two of the diagnostics the contract requires can only be produced here. Avalonia reports both
/// as a failure of the whole document, with a position and a message about an emitter — a
/// handler that does not exist and a mistyped markup extension both read as "the file is broken"
/// rather than as "this attribute is wrong".
/// </para>
/// <para>
/// A missing handler is also removed from the projected text, so that the rest of the document
/// still loads. An event that names a method nobody has written yet is the ordinary state of a
/// file being worked on, and losing the whole tree over it is the wrong trade for an editor.
/// A markup extension is reported and left alone: dropping a value would change what the
/// document says rather than what it can be given to Avalonia as.
/// </para>
/// </remarks>
internal static class XamlAttributeChecks
{
    /// <summary>Checks a document's attributes against the types they will be applied to.</summary>
    /// <param name="document">The document about to be loaded.</param>
    /// <param name="rootType">The type whose methods an event handler names, when there is one.</param>
    /// <param name="environment">The environment the document's names are resolved through.</param>
    /// <param name="diagnostics">Collects everything noticed on the way.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>The attributes the projection has to leave out for the load to survive.</returns>
    internal static async ValueTask<ImmutableArray<TextSpan>> RunAsync(
        XamlDocument document,
        Type? rootType,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ImmutableArray<TextSpan>.Builder removals = ImmutableArray.CreateBuilder<TextSpan>();

        foreach (XamlElement element in document.DescendantElements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A property element is a member of its parent, not a type of its own.
            if (element.IsPropertyElementSyntax || element.NamespaceUri is not { } namespaceUri)
            {
                continue;
            }

            Type? type = (await environment.TypeResolver
                    .ResolveAsync(new XamlTypeName(namespaceUri, element.Name.LocalName), element.NamespaceContext, cancellationToken)
                    .ConfigureAwait(false))
                .Type;

            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute is XamlNamespaceDeclaration
                    || attribute.IsDirective
                    || attribute.IsDesignTime
                    || attribute.IsMarkupCompatibility)
                {
                    continue;
                }

                await CheckAsync(
                        document, attribute, type, rootType, environment, diagnostics, removals, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return removals.ToImmutable();
    }

    private static async ValueTask CheckAsync(
        XamlDocument document,
        XamlAttribute attribute,
        Type? type,
        Type? rootType,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        ImmutableArray<TextSpan>.Builder removals,
        CancellationToken cancellationToken)
    {
        if (attribute.GetValue() is XamlMarkupExtensionValue extension)
        {
            await CheckExtensionAsync(document, attribute, extension, environment, diagnostics, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        if (type is null)
        {
            // The element's own type is unresolved and already reported as such. Nothing can be
            // said about its members that is not just that fact again.
            return;
        }

        if (XamlMemberResolver.Instance.Resolve(type, attribute.Name.LocalName).Kind != XamlMemberKind.Event)
        {
            return;
        }

        string handler = attribute.GetValueText();

        if (rootType is not null && HasHandler(rootType, handler))
        {
            return;
        }

        diagnostics.Add(MarkupDiagnostic.Load(
            XamlLoaderDiagnosticCodes.MissingEventHandler,
            rootType is null
                ? $"'{attribute.Name}' names the handler '{handler}', but the document has no x:Class to find it on."
                : $"'{attribute.Name}' names the handler '{handler}', which {rootType.Name} does not declare.",
            MarkupDiagnosticSeverity.Warning,
            document.Uri,
            attribute.Span));

        removals.Add(attribute.Span);
    }

    /// <summary>
    /// Checks that a markup extension names something that exists.
    /// </summary>
    /// <remarks>
    /// XAML's own extensions — <c>x:Static</c>, <c>x:Type</c>, <c>x:Null</c> — are language, not
    /// types, and are left alone. For the rest the convention is that <c>{Foo}</c> is the type
    /// <c>Foo</c> or <c>FooExtension</c>, so failing to find either is a name that will not
    /// resolve however the document is loaded.
    /// </remarks>
    private static async ValueTask CheckExtensionAsync(
        XamlDocument document,
        XamlAttribute attribute,
        XamlMarkupExtensionValue extension,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (attribute.Parent is not XamlElement element
            || element.NamespaceContext.LookupNamespace(extension.TypeName.Prefix) is not { } namespaceUri
            || string.Equals(namespaceUri, XamlNamespaces.Xaml, StringComparison.Ordinal))
        {
            return;
        }

        foreach (string candidate in new[] { extension.TypeName.LocalName, extension.TypeName.LocalName + "Extension" })
        {
            XamlTypeResolution resolution = await environment.TypeResolver
                .ResolveAsync(new XamlTypeName(namespaceUri, candidate), element.NamespaceContext, cancellationToken)
                .ConfigureAwait(false);

            if (resolution.Success)
            {
                return;
            }
        }

        diagnostics.Add(MarkupDiagnostic.Resolution(
            XamlLoaderDiagnosticCodes.MarkupExtensionFailure,
            $"'{extension.TypeName}' is not a markup extension anything in scope declares.",
            MarkupDiagnosticSeverity.Warning,
            document.Uri,
            attribute.ValueSpan ?? attribute.Span));
    }

    /// <summary>
    /// Reports whether a type declares a method an event handler could name.
    /// </summary>
    /// <remarks>
    /// By name only. Whether the signature matches is Avalonia's to decide when it hooks the
    /// handler up, and guessing at delegate compatibility here would produce a second, less
    /// informed opinion about the same question.
    /// </remarks>
    private static bool HasHandler(Type rootType, string handler) =>
        !string.IsNullOrWhiteSpace(handler)
        && rootType
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.FlattenHierarchy)
            .Any(method => string.Equals(method.Name, handler, StringComparison.Ordinal));
}
