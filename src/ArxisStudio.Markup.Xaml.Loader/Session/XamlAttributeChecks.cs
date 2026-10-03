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
/// <para>
/// An <c>x:Class</c> the load cannot use goes the same way, for the same reason. The class was
/// reported and the load carried on without it — but the directive still went to Avalonia, whose
/// loader resolves it on its own and fails the whole document over a class nobody has built yet,
/// which is the first state a new form is in. Without it the root is built as the element it is
/// written as.
/// </para>
/// <para>
/// What this returns has to be what every projection of one session leaves out, not only the
/// first. An update rebuilds part of the document from a projection of its own, and a projection
/// that put back what the load withheld would fail where the load succeeded — or, for a source
/// update, differ from the load's text when nothing has changed.
/// </para>
/// <para>
/// The handlers the class does answer are returned too. A load hands them to Avalonia, which hooks
/// them up to the instance it populates; a part an update rebuilds has no instance to be hooked up
/// to, so the update leaves them out of that part's text and hooks them up itself.
/// </para>
/// </remarks>
internal static class XamlAttributeChecks
{
    /// <summary>Checks a document's attributes against the types they will be applied to.</summary>
    /// <param name="document">The document about to be loaded.</param>
    /// <param name="rootType">
    /// The class the document populates, whose methods an event handler names — or
    /// <see langword="null"/> when there is none to use, in which case the document's
    /// <c>x:Class</c>, if it has one, is withheld as well.
    /// </param>
    /// <param name="classUse">
    /// Whether the host asked for the class at all, which is what a handler with nothing to be hooked
    /// up to is told it lacks.
    /// </param>
    /// <param name="environment">The environment the document's names are resolved through.</param>
    /// <param name="diagnostics">Collects everything noticed on the way.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>
    /// The attributes the projection has to leave out for the load to survive, and the handlers the
    /// class answers.
    /// </returns>
    internal static async ValueTask<XamlAttributeFindings> RunAsync(
        XamlDocument document,
        Type? rootType,
        XamlClassUse classUse,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ImmutableArray<TextSpan>.Builder removals = ImmutableArray.CreateBuilder<TextSpan>();
        ImmutableArray<XamlHandlerAttribute>.Builder handlers = ImmutableArray.CreateBuilder<XamlHandlerAttribute>();

        // Reported where it was resolved — unresolved, or not what the root says it is. Here it is
        // only kept out of the text, so that Avalonia does not go looking for it a second time.
        if (rootType is null && document.Root?.GetDirectiveAttribute(XamlDirectives.Class) is { } named)
        {
            removals.Add(named.Span);
        }

        foreach (XamlElement element in document.DescendantElements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A property element is a member of its parent, not a type of its own.
            if (element.IsPropertyElementSyntax)
            {
                continue;
            }

            if (element.NamespaceUri is not { } namespaceUri)
            {
                // A prefix nothing declares is worth saying out loud. Avalonia's own failure for
                // it names the element and not the prefix, and the load stops there, so a
                // document one missing xmlns away from working reported something else entirely.
                // An unprefixed name resolves against whatever default is in scope and is not
                // this diagnostic's business.
                if (element.Name.Prefix is { } prefix)
                {
                    diagnostics.Add(MarkupDiagnostic.Resolution(
                        XamlLoaderDiagnosticCodes.UndeclaredPrefix,
                        $"The prefix '{prefix}' on '{element.Name}' is not declared anywhere in scope. "
                            + $"Declare it with an xmlns:{prefix} attribute.",
                        MarkupDiagnosticSeverity.Error,
                        document.Uri,
                        element.NameSpan));
                }

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
                        document, element, attribute, type, rootType, classUse, environment, diagnostics, removals,
                        handlers, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new XamlAttributeFindings(removals.ToImmutable(), handlers.ToImmutable());
    }

    private static async ValueTask CheckAsync(
        XamlDocument document,
        XamlElement element,
        XamlAttribute attribute,
        Type? type,
        Type? rootType,
        XamlClassUse classUse,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        ImmutableArray<TextSpan>.Builder removals,
        ImmutableArray<XamlHandlerAttribute>.Builder handlers,
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

        if (environment.MemberResolver.Resolve(type, attribute.Name.LocalName).Kind != XamlMemberKind.Event)
        {
            return;
        }

        string handler = attribute.GetValueText();

        if (rootType is not null && HasHandler(rootType, handler))
        {
            handlers.Add(new XamlHandlerAttribute(element, attribute, handler));

            return;
        }

        diagnostics.Add(MarkupDiagnostic.Load(
            XamlLoaderDiagnosticCodes.MissingEventHandler,
            rootType is not null
                ? $"'{attribute.Name}' names the handler '{handler}', which {rootType.Name} does not declare."
                : classUse == XamlClassUse.AsWritten
                    ? $"'{attribute.Name}' names the handler '{handler}', and the root is built as written, " +
                      "without the class that would answer it."
                    : $"'{attribute.Name}' names the handler '{handler}', but the document has no x:Class to find it on.",
            MarkupDiagnosticSeverity.Warning,
            document.Uri,
            attribute.Span));

        removals.Add(attribute.Span);
    }

    /// <summary>The namespaces whose markup extensions this cannot have an opinion about.</summary>
    /// <remarks>
    /// XAML's own — <c>x:Static</c>, <c>x:Type</c>, <c>x:Null</c> — are language rather than
    /// types. Avalonia's own are types, but its compiler finds them through machinery this
    /// library does not model: <c>IXamlTypeResolver</c> resolves <c>UserControl</c> from the
    /// Avalonia namespace and not <c>Binding</c> from the same one, so a check based on it would
    /// report the single most common construct in Avalonia XAML as an error. A diagnostic with
    /// no discriminating power is worse than none.
    /// </remarks>
    private static readonly string[] NotOurs =
        [XamlNamespaces.Xaml, "https://github.com/avaloniaui", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"];

    /// <summary>
    /// Checks that a markup extension names something that exists.
    /// </summary>
    /// <remarks>
    /// Only where the environment's type resolver is the same authority the load will use: an
    /// extension in a namespace the document itself brought in, where <c>{Foo}</c> is by
    /// convention the type <c>Foo</c> or <c>FooExtension</c> and failing to find either is a name
    /// that will not resolve however the document is loaded. See <see cref="NotOurs"/> for the
    /// namespaces this stands down on, and why.
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
            || NotOurs.Contains(namespaceUri, StringComparer.Ordinal))
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
