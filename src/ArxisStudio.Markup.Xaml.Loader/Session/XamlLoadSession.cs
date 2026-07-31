using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// A document and the live Avalonia objects created from it.
/// </summary>
/// <remarks>
/// <para>
/// Loading a document is executable behaviour. It runs constructors, property setters, type
/// converters, markup extensions, resource factories and any custom control code the document
/// reaches. This library provides no sandbox and makes no attempt to be one — a caller loading
/// XAML it did not write is running code it did not write.
/// </para>
/// <para>
/// Object creation happens on the thread the environment's dispatcher owns. Avalonia objects
/// have thread affinity, and touching one from elsewhere corrupts state that surfaces much
/// later and somewhere else.
/// </para>
/// <para>
/// Objects are built by Avalonia's own public runtime loader. Nothing here reimplements or
/// forks its compiler.
/// </para>
/// </remarks>
public sealed partial class XamlLoadSession : IAsyncDisposable
{
    private readonly IXamlDispatcher _dispatcher;

    private bool _disposed;

    private XamlLoadSession(
        XamlDocument document,
        XamlLoadEnvironment environment,
        XamlLoadOptions options,
        TextProjection projection,
        object rootObject,
        ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Document = document;
        Environment = environment;
        Projection = projection;
        Objects = XamlObjectMap.Build(document, rootObject, projection);
        Options = options;
        RootObject = rootObject;
        Diagnostics = diagnostics;
        _dispatcher = environment.Dispatcher;
    }

    /// <summary>
    /// Gets the document the objects were created from.
    /// </summary>
    /// <remarks>
    /// Advances as edits are applied, because editing reparses. Elements taken from an earlier
    /// value describe text that has moved and are rejected if used.
    /// </remarks>
    public XamlDocument Document { get; private set; }

    /// <summary>Gets which element of the document each loaded object came from.</summary>
    public XamlObjectMap Objects { get; private set; }

    /// <summary>
    /// Gets the text the objects were actually built from: the document with its resource and
    /// style includes resolved through the environment and spliced in.
    /// </summary>
    /// <remarks>
    /// Derived, never saved, and not a second version of the document. It exists because
    /// Avalonia resolves includes through its own asset loader during the load and offers no
    /// seam for the environment's resolvers; expanding them into the text handed over is how
    /// they get one. <see cref="TextProjection"/> carries the map back, which is what keeps
    /// positions Avalonia reports attributable to the file they are really in.
    /// </remarks>
    public TextProjection Projection { get; }

    /// <summary>Gets the environment the document was loaded through.</summary>
    public XamlLoadEnvironment Environment { get; }

    /// <summary>Gets the options the document was loaded with.</summary>
    public XamlLoadOptions Options { get; }

    /// <summary>Gets the object the document produced.</summary>
    public object RootObject { get; }

    /// <summary>Gets everything noticed while loading.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; private set; }

    /// <summary>
    /// Loads a document, creating the objects it describes.
    /// </summary>
    /// <remarks>
    /// Throws only when there is no session to hand back. Problems the load survived are
    /// reported through <see cref="Diagnostics"/>; use
    /// <see cref="TryCreateAsync(XamlDocument, XamlLoadEnvironment, XamlLoadOptions?, CancellationToken)"/>
    /// to get the diagnostics for a load that produced nothing.
    /// </remarks>
    /// <param name="document">The document to load.</param>
    /// <param name="environment">Everything outside the document that loading needs.</param>
    /// <param name="options">How to load, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to observe while loading.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="environment"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The document produced no object.</exception>
    public static async ValueTask<XamlLoadSession> CreateAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        XamlLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        (XamlLoadSession? session, XamlLoadResult result) =
            await TryCreateAsync(document, environment, options, cancellationToken).ConfigureAwait(false);

        return session ?? throw new InvalidOperationException(
            "The document produced no object. " + Describe(result.Diagnostics));
    }

    /// <summary>Loads a document, returning the diagnostics even when nothing was produced.</summary>
    /// <param name="document">The document to load.</param>
    /// <param name="environment">Everything outside the document that loading needs.</param>
    /// <param name="options">How to load, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to observe while loading.</param>
    /// <returns>The session when one was produced, and what the load found either way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="environment"/> is <see langword="null"/>.</exception>
    public static async ValueTask<(XamlLoadSession? Session, XamlLoadResult Result)> TryCreateAsync(
        XamlDocument document,
        XamlLoadEnvironment environment,
        XamlLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(environment);

        options ??= XamlLoadOptions.Default;

        var diagnostics = new List<MarkupDiagnostic>();

        // A document that did not parse cleanly will not load cleanly either, and saying so
        // with the syntax diagnostics is more use than whatever Avalonia makes of the text.
        foreach (MarkupDiagnostic diagnostic in document.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        // Includes are resolved before anything is created, because Avalonia resolves them
        // itself during the load and throws when it cannot. Resolving them here through the
        // environment, and handing over text with their content already in place, is what makes
        // the caller's own providers the ones that answer.
        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(document, environment, diagnostics, cancellationToken).ConfigureAwait(false);

        // x:Class has to be resolved and instantiated before loading, because Avalonia
        // populates an instance the caller supplies rather than creating one for it. This also
        // gives Avalonia the object whose methods the document's event handlers name.
        object? rootInstance = await environment.Dispatcher
            .InvokeAsync(
                () => XamlRootClass
                    .CreateInstanceAsync(document, environment, options, diagnostics, cancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult(),
                cancellationToken)
            .ConfigureAwait(false);

        object? root = await environment.Dispatcher
            .InvokeAsync(() => Load(document, projection, options, rootInstance, diagnostics), cancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            return (null, new XamlLoadResult { RootObject = null, Diagnostics = [.. diagnostics] });
        }

        var session = new XamlLoadSession(document, environment, options, projection, root, []);

        // Design-time values are applied after the objects exist, because that is the earliest
        // there is anything to apply them to, and through the map, because an attribute belongs
        // to the object its own element produced rather than to the root.
        if (options.Mode == XamlLoadMode.Design)
        {
            await environment.Dispatcher
                .InvokeAsync<object?>(
                    () =>
                    {
                        XamlDesignValues.Apply(document, session.Objects, root, diagnostics);

                        return null;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var result = new XamlLoadResult { RootObject = root, Diagnostics = [.. diagnostics] };

        session.Diagnostics = result.Diagnostics;

        return (session, result);
    }

    /// <summary>
    /// Throws if the caller is not on the thread that owns this session's objects.
    /// </summary>
    /// <remarks>
    /// Called by every operation that reaches an Avalonia object. Failing here, with a clear
    /// message, beats letting the corruption surface somewhere unrelated later.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "This session's Avalonia objects belong to another thread. Marshal the call through " +
                $"{nameof(XamlLoadEnvironment)}.{nameof(XamlLoadEnvironment.Dispatcher)} instead of touching them directly.");
        }
    }

    /// <summary>Gets the root object as a given type.</summary>
    /// <typeparam name="T">The type to cast to.</typeparam>
    /// <returns>The root object.</returns>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects, or the root is not that type.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public T GetRoot<T>()
        where T : class
    {
        VerifyAccess();

        return RootObject as T ?? throw new InvalidOperationException(
            $"The document's root is {RootObject.GetType().FullName}, which is not {typeof(T).FullName}.");
    }

    /// <summary>Releases the session.</summary>
    /// <remarks>
    /// The objects themselves are the caller's, and are not torn down here: a caller routinely
    /// keeps the tree after the session that built it has gone.
    /// </remarks>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        _disposed = true;

        return ValueTask.CompletedTask;
    }

    /// <summary>Hands the projected text to Avalonia's runtime loader.</summary>
    private static object? Load(
        XamlDocument document,
        TextProjection projection,
        XamlLoadOptions options,
        object? rootInstance,
        List<MarkupDiagnostic> diagnostics)
    {
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = options.LocalAssembly,
            UseCompiledBindingsByDefault = options.UseCompiledBindingsByDefault,
            DesignMode = options.Mode == XamlLoadMode.Design,

            // The object map is built from the source information Avalonia records here.
            CreateSourceInfo = true,
            DiagnosticHandler = diagnostic =>
            {
                diagnostics.Add(Translate(diagnostic, document, projection));

                // The handler's return value is the severity Avalonia goes on to use. Echoing
                // what it decided keeps this a report rather than an intervention.
                return diagnostic.Severity;
            },
        };

        // The root-instance constructor is what makes event handlers resolvable: Avalonia looks
        // for the named methods on the object it is populating.
        var loaderDocument = new RuntimeXamlLoaderDocument(
            document.BaseUri, rootInstance, projection.Text.ToString());

        try
        {
            return AvaloniaRuntimeXamlLoader.Load(loaderDocument, configuration);
        }
        catch (Exception error)
        {
            // Creating objects runs user code, which may throw for reasons that have nothing to
            // do with the document being malformed. The caller gets a diagnostic and whatever
            // else the load found, rather than an exception carrying only the last failure.
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.ObjectCreationFailure,
                $"Creating objects from the document failed: {error.Message}",
                MarkupDiagnosticSeverity.Error,
                document.Uri));

            return null;
        }
    }

    /// <summary>
    /// Turns one of Avalonia's diagnostics into one of this library's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia reports a line and column; this library reports spans. Mapping them here means
    /// a caller can highlight the offending text without knowing where the diagnostic came
    /// from.
    /// </para>
    /// <para>
    /// The line and column are in the projected text, where an include has been replaced by the
    /// file it names. Sending them through the projection is what stops a fault in an included
    /// dictionary being reported against whichever line of this document happens to sit at the
    /// same number.
    /// </para>
    /// </remarks>
    private static MarkupDiagnostic Translate(
        RuntimeXamlDiagnostic diagnostic,
        XamlDocument document,
        TextProjection projection)
    {
        Uri? documentUri = document.Uri;
        TextSpan? span = null;

        if (diagnostic.LineNumber is { } line && line > 0)
        {
            TextProjectionPosition position = projection.Map(
                new TextPosition(line - 1, Math.Max(0, (diagnostic.LinePosition ?? 1) - 1)));

            documentUri = position.IsOriginal ? document.Uri : position.SourceUri;
            span = new TextSpan(position.Offset, 0);
        }

        return MarkupDiagnostic.Load(
            XamlLoaderDiagnosticCodes.RuntimeLoadFailure,
            $"{diagnostic.Id}: {diagnostic.Title}",
            diagnostic.Severity switch
            {
                RuntimeXamlDiagnosticSeverity.Info => MarkupDiagnosticSeverity.Info,
                RuntimeXamlDiagnosticSeverity.Warning => MarkupDiagnosticSeverity.Warning,
                _ => MarkupDiagnosticSeverity.Error,
            },
            documentUri,
            span);
    }

    private static string Describe(ImmutableArray<MarkupDiagnostic> diagnostics) =>
        diagnostics.IsEmpty
            ? "The load reported nothing further."
            : string.Join(" ", diagnostics.Select(static diagnostic => diagnostic.ToString()));
}
