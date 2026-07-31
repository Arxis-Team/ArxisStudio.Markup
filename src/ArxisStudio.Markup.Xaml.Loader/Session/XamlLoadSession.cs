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
        object rootObject,
        ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        Document = document;
        Environment = environment;
        Objects = XamlObjectMap.Build(document, rootObject);
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

    /// <summary>Gets the environment the document was loaded through.</summary>
    public XamlLoadEnvironment Environment { get; }

    /// <summary>Gets the options the document was loaded with.</summary>
    public XamlLoadOptions Options { get; }

    /// <summary>Gets the object the document produced.</summary>
    public object RootObject { get; }

    /// <summary>Gets everything noticed while loading.</summary>
    public ImmutableArray<MarkupDiagnostic> Diagnostics { get; }

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
            .InvokeAsync(() => Load(document, options, rootInstance, diagnostics), cancellationToken)
            .ConfigureAwait(false);

        var result = new XamlLoadResult { RootObject = root, Diagnostics = [.. diagnostics] };

        return root is null
            ? (null, result)
            : (new XamlLoadSession(document, environment, options, root, result.Diagnostics), result);
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

    /// <summary>Hands the document's text to Avalonia's runtime loader.</summary>
    private static object? Load(
        XamlDocument document,
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
                diagnostics.Add(Translate(diagnostic, document));

                // The handler's return value is the severity Avalonia goes on to use. Echoing
                // what it decided keeps this a report rather than an intervention.
                return diagnostic.Severity;
            },
        };

        // The root-instance constructor is what makes event handlers resolvable: Avalonia looks
        // for the named methods on the object it is populating.
        var loaderDocument = new RuntimeXamlLoaderDocument(document.BaseUri, rootInstance, document.GetText());

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
    /// Avalonia reports a line and column; this library reports spans. Mapping them here means
    /// a caller can highlight the offending text without knowing where the diagnostic came
    /// from.
    /// </remarks>
    private static MarkupDiagnostic Translate(RuntimeXamlDiagnostic diagnostic, XamlDocument document)
    {
        TextSpan? span = null;

        if (diagnostic.LineNumber is { } line && line > 0)
        {
            int zeroBasedLine = Math.Min(line - 1, document.SourceText.Lines.Count - 1);
            TextLine textLine = document.SourceText.Lines[zeroBasedLine];

            int column = Math.Clamp((diagnostic.LinePosition ?? 1) - 1, 0, textLine.Span.Length);

            span = new TextSpan(textLine.Start + column, 0);
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
            document.Uri,
            span);
    }

    private static string Describe(ImmutableArray<MarkupDiagnostic> diagnostics) =>
        diagnostics.IsEmpty
            ? "The load reported nothing further."
            : string.Join(" ", diagnostics.Select(static diagnostic => diagnostic.ToString()));
}
