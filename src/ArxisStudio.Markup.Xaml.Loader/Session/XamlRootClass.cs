using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Turns a document's <c>x:Class</c> into the object the document will populate.
/// </summary>
/// <remarks>
/// <para>
/// Every step reports rather than throws. A document naming a class that has not been compiled
/// yet is ordinary in an editor, and refusing to show anything would be worse than showing the
/// markup with a diagnostic beside it.
/// </para>
/// <para>
/// In two halves, because they belong on different threads. <see cref="ResolveAsync"/> asks the
/// environment's resolver which type the name means: metadata, no Avalonia object, nothing the
/// owning thread has any claim on. <see cref="CreateAsync"/> runs the class's constructor, which
/// makes Avalonia objects and may compile markup of its own — so it belongs on the owning thread,
/// inside the environment's compilation scope. Keeping the resolver's half off that thread is
/// what stops a host whose resolver marshals <em>to</em> it from waiting on itself.
/// </para>
/// </remarks>
internal static class XamlRootClass
{
    /// <summary>Reads the <c>x:Class</c> directive, if the document has one.</summary>
    public static string? DirectiveOf(XamlDocument document) =>
        document.Root?.GetDirective(XamlDirectives.Class);

    /// <summary>
    /// Resolves the class and checks it against the root element.
    /// </summary>
    /// <returns>
    /// The type to create, or <see langword="null"/> when the document names none or names one
    /// that cannot be used — in which case the load carries on without it and the diagnostics
    /// say which of the two it was.
    /// </returns>
    public static async ValueTask<Type?> ResolveAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        string? className = DirectiveOf(document);

        if (string.IsNullOrWhiteSpace(className) || document.Root is not { } root)
        {
            return null;
        }

        Type? rootType = await ResolveClassAsync(className, root, environment, cancellationToken)
            .ConfigureAwait(false);

        if (rootType is null)
        {
            diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.UnresolvedRootType,
                $"x:Class names '{className}', which was not found in any assembly supplied to the environment.",
                MarkupDiagnosticSeverity.Error,
                document.Uri,
                root.GetDirectiveAttribute(XamlDirectives.Class)?.ValueSpan));

            return null;
        }

        if (!await IsCompatibleAsync(rootType, root, environment, cancellationToken).ConfigureAwait(false))
        {
            diagnostics.Add(MarkupDiagnostic.Resolution(
                XamlLoaderDiagnosticCodes.IncompatibleRootType,
                $"x:Class names '{rootType.FullName}', which does not derive from the root element's type " +
                $"'{root.Name}'. The document cannot populate it.",
                MarkupDiagnosticSeverity.Error,
                document.Uri,
                root.GetDirectiveAttribute(XamlDirectives.Class)?.ValueSpan));

            return null;
        }

        return rootType;
    }

    /// <summary>
    /// Creates the instance the document will populate.
    /// </summary>
    /// <remarks>
    /// Runs the caller's factory, and through it the class's constructor, so the caller is on the
    /// thread that owns Avalonia objects and inside the environment's compilation scope.
    /// </remarks>
    /// <returns>
    /// The instance, or <see langword="null"/> when there is no usable one — in which case the
    /// load carries on without it and the diagnostics say why.
    /// </returns>
    public static async ValueTask<object?> CreateAsync(
        Type rootType,
        XamlDocument document,
        XamlLoadEnvironment environment,
        XamlLoadOptions options,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        IXamlRootInstanceFactory factory = environment.RootInstanceFactory ?? DefaultXamlRootInstanceFactory.Instance;
        var context = new XamlRootInstanceContext(document, options.Mode);

        try
        {
            // Keeps its context deliberately, where the rest of these packages drop theirs. A
            // factory that does not finish synchronously resumes here, and here is inside the
            // caller's compilation scope, on the thread that entered it and will leave it —
            // dropping the context would move both to whichever thread the pool offered.
            object instance = await factory.CreateAsync(rootType, context, cancellationToken).ConfigureAwait(true);

            if (!rootType.IsInstanceOfType(instance))
            {
                diagnostics.Add(MarkupDiagnostic.Load(
                    XamlLoaderDiagnosticCodes.InvalidRootFactoryResult,
                    $"The root-instance factory returned {instance.GetType().FullName}, which is not " +
                    $"'{rootType.FullName}'.",
                    MarkupDiagnosticSeverity.Error,
                    document.Uri));

                return null;
            }

            return instance;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Creating the instance runs the caller's constructor, which can fail for reasons
            // that have nothing to do with the document.
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.RootInstanceCreationFailure,
                $"Creating an instance of '{rootType.FullName}' failed: {error.Message}",
                MarkupDiagnosticSeverity.Error,
                document.Uri));

            return null;
        }
    }

    /// <summary>Resolves a fully qualified class name through the environment's type resolver.</summary>
    private static async ValueTask<Type?> ResolveClassAsync(
        string className,
        XamlElement root,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        int lastDot = className.LastIndexOf('.');

        // A class with no namespace is legal, and resolving it means searching every assembly
        // in play rather than a named one.
        string clrNamespace = lastDot < 0 ? string.Empty : className[..lastDot];
        string localName = lastDot < 0 ? className : className[(lastDot + 1)..];

        var name = new XamlTypeName($"using:{clrNamespace}", localName);

        XamlTypeResolution resolution = await environment.TypeResolver
            .ResolveAsync(name, root.NamespaceContext, cancellationToken).ConfigureAwait(false);

        return resolution.Type;
    }

    /// <summary>
    /// Checks that the class actually is the kind of thing the root element declares.
    /// </summary>
    /// <remarks>
    /// A document whose root reads <c>&lt;UserControl x:Class="…"&gt;</c> populates the class as
    /// a <c>UserControl</c>. If the class is not one, Avalonia's loader would fail somewhere
    /// inside itself; checking first means the caller is told which of the two names is wrong.
    /// </remarks>
    private static async ValueTask<bool> IsCompatibleAsync(
        Type rootType,
        XamlElement root,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (root.NamespaceUri is not { } namespaceUri)
        {
            // The root element's own prefix does not resolve, so there is no declared type to
            // check against. That is already reported as a syntax problem.
            return true;
        }

        XamlTypeResolution declared = await environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, root.Name.LocalName), root.NamespaceContext, cancellationToken)
            .ConfigureAwait(false);

        // An unresolvable root element type is reported elsewhere; not being able to check is
        // not the same as having checked and failed.
        return !declared.Success || declared.Type.IsAssignableFrom(rootType);
    }
}
