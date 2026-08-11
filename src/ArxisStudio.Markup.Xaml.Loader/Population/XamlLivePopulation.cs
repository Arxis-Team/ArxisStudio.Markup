using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Populates instances of compiled <c>x:Class</c> controls from live documents instead of the
/// markup compiled into their assembly.
/// </summary>
/// <remarks>
/// <para>
/// A control placed inside another document — <c>&lt;views:MyControl /&gt;</c> — is constructed by
/// Avalonia, and its constructor loads the markup that was <em>compiled</em> into it. No resolver
/// of this library's is consulted on the way: however current the outer document is kept, the
/// embedded control keeps the shape it had when its project was last built. This type is the seam
/// that changes where that markup comes from.
/// </para>
/// <para>
/// The seam is Avalonia's own. Its XAML compiler emits, into every <c>x:Class</c> type, a static
/// <c>!XamlIlPopulateOverride</c> field that the generated <c>InitializeComponent</c> consults
/// before the compiled markup — the hook its hot-reload tooling is built on. Registering a
/// document installs a delegate there that hands the document's projected text to
/// <see cref="AvaloniaRuntimeXamlLoader"/> over the instance being constructed, so every
/// instance made after that — in any session, however deeply nested — carries the document as it
/// is now. See ADR 0014 for why reaching a generated member by name is acceptable here and what
/// happens when a future Avalonia stops emitting it: registration reports
/// <see cref="XamlLoaderDiagnosticCodes.NotPopulatable"/> and the compiled behaviour simply
/// remains.
/// </para>
/// <para>
/// Population must not be able to break a form that places the control. An instance whose live
/// document fails to compile — mid-edit documents routinely do not — is populated from the
/// compiled markup instead, and the failure is reported through <see cref="PopulationFailed"/>:
/// stale content over no content. The same fallback breaks cycles, where two live documents each
/// place the other's control and construction would otherwise never bottom out.
/// </para>
/// <para>
/// The projected text is prepared when a document is registered, not when an instance is
/// constructed: preparation resolves includes through the environment's resolvers, which is
/// asynchronous work, and construction happens inside somebody's constructor, which is not a
/// place to wait. What runs at construction time is one runtime compilation, inside the
/// environment's <see cref="IXamlCompilationScope"/>, on the constructing thread — which is the
/// thread the instance belongs to.
/// </para>
/// <para>
/// The override field is process-wide state on the type, which is why registrations are owned:
/// disposing this object, or removing a type, puts the field back so the compiled markup answers
/// again — and a field some other party has since claimed is left alone.
/// </para>
/// </remarks>
public sealed class XamlLivePopulation : IDisposable
{
    /// <summary>The field Avalonia's XAML compiler emits for exactly this purpose.</summary>
    private const string OverrideFieldName = "!XamlIlPopulateOverride";

    /// <summary>The generated method that consults it and runs the compiled markup otherwise.</summary>
    private const string TrampolineMethodName = "!XamlIlPopulateTrampoline";

    /// <summary>
    /// The types the current thread is populating, for breaking cycles.
    /// </summary>
    /// <remarks>
    /// Thread-static because population is re-entrant by design — a live document that places
    /// another registered control populates it in the middle of its own population — and the only
    /// re-entrancy that must be stopped is the same type appearing inside itself.
    /// </remarks>
    [ThreadStatic]
    private static HashSet<Type>? t_populating;

    private readonly XamlLoadEnvironment _environment;
    private readonly XamlLivePopulationOptions _options;
    private readonly Dictionary<Type, Registration> _registrations = [];
    private readonly Lock _gate = new();

    private int _disposed;

    /// <summary>Creates the service over the environment population compiles in.</summary>
    /// <param name="environment">
    /// Where includes are resolved and whose <see cref="XamlLoadEnvironment.CompilationScope"/>
    /// brackets every compilation — the same environment the documents' own sessions load in, or
    /// the two would disagree about which assemblies a name means.
    /// </param>
    /// <param name="options">How to populate, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> is <see langword="null"/>.</exception>
    public XamlLivePopulation(XamlLoadEnvironment environment, XamlLivePopulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _environment = environment;
        _options = options ?? XamlLivePopulationOptions.Default;
    }

    /// <summary>
    /// Raised when an instance was populated from its compiled markup because the live document
    /// could not do it — a compile failure, or a cycle of controls placing each other.
    /// </summary>
    /// <remarks>
    /// Raised on whatever thread was constructing the instance. A subscriber that throws is
    /// isolated, because it is arbitrary host code running inside somebody's constructor.
    /// </remarks>
    public event EventHandler<XamlLivePopulationFailedEventArgs>? PopulationFailed;

    /// <summary>Gets how many types currently populate from a live document.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _registrations.Count;
            }
        }
    }

    /// <summary>Whether instances of a type currently populate from a live document.</summary>
    /// <param name="type">The type to ask about.</param>
    /// <returns><see langword="true"/> when a document is registered for it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public bool Contains(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (_gate)
        {
            return _registrations.ContainsKey(type);
        }
    }

    /// <summary>
    /// Registers a document as what instances of a type are populated from, replacing the
    /// document registered before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instances already on screen are not touched — population happens when an instance is
    /// constructed, so what this changes is every construction from now on. A host that wants an
    /// open preview to show the new document rebuilds that preview, which is the host's call to
    /// make: it knows what is on screen and this does not.
    /// </para>
    /// <para>
    /// The document is prepared here — includes resolved, design-time attributes stripped,
    /// attributes nothing could load removed — and the diagnostics of that preparation are the
    /// result's. A document that prepares with errors is still installed: it may well compile
    /// regardless, and if it does not, population falls back to the compiled markup and says so
    /// through <see cref="PopulationFailed"/>. The one thing that refuses installation is a type
    /// with no compiled markup to stand in for, reported as
    /// <see cref="XamlLoaderDiagnosticCodes.NotPopulatable"/>.
    /// </para>
    /// </remarks>
    /// <param name="type">The compiled type whose instances the document should populate.</param>
    /// <param name="document">The document to populate them from.</param>
    /// <param name="cancellationToken">A token to observe while preparing.</param>
    /// <returns>Whether the override is in place, and what preparing found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This service has been disposed.</exception>
    public async ValueTask<XamlLivePopulationResult> SetDocumentAsync(
        Type type, XamlDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var diagnostics = new List<MarkupDiagnostic>();

        // The document's own parse problems travel with the result, the way a load's would: the
        // caller registering a document mid-edit wants to know what state it is in.
        diagnostics.AddRange(document.Diagnostics);

        if (Hooks(type) is not { } hooks)
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.NotPopulatable,
                $"'{type.FullName}' carries no compiled markup to stand in for. Only a type built " +
                "by Avalonia's XAML compiler from an x:Class document can be populated live.",
                MarkupDiagnosticSeverity.Error,
                document.Uri));

            return new XamlLivePopulationResult { Installed = false, Diagnostics = [.. diagnostics] };
        }

        if (document.Root?.GetDirective(XamlDirectives.Class) is { Length: > 0 } declared
            && !string.Equals(declared, type.FullName, StringComparison.Ordinal))
        {
            diagnostics.Add(MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.LivePopulationClassMismatch,
                $"The document's x:Class names '{declared}' but it was registered for " +
                $"'{type.FullName}'. Avalonia refuses to populate an instance from a document " +
                "naming another class, so instances will fall back to their compiled markup.",
                MarkupDiagnosticSeverity.Warning,
                document.Uri,
                document.Root.GetDirectiveAttribute(XamlDirectives.Class)?.ValueSpan));
        }

        // The same preparation a session load performs, for the same reasons: an event attribute
        // naming a handler the type does not have would fail the whole population, and includes
        // have to be resolved through the environment because Avalonia offers no seam for them.
        ImmutableArray<TextSpan> unloadable = await XamlAttributeChecks
            .RunAsync(document, type, _environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(document, null, _environment, diagnostics, unloadable, cancellationToken)
            .ConfigureAwait(false);

        var prepared = new PreparedDocument(document, document.BaseUri, projection.Text.ToString());

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            if (_registrations.TryGetValue(type, out Registration? existing))
            {
                Volatile.Write(ref existing.Prepared, prepared);
            }
            else
            {
                var registration = new Registration(this, type, hooks.Field, hooks.Trampoline, prepared);

                _registrations.Add(type, registration);
                hooks.Field.SetValue(null, registration.Override);
            }
        }

        return new XamlLivePopulationResult { Installed = true, Diagnostics = [.. diagnostics] };
    }

    /// <summary>
    /// Removes a type's registration, so its instances populate from the compiled markup again.
    /// </summary>
    /// <param name="type">The type to release.</param>
    /// <returns><see langword="true"/> when a registration was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public bool Remove(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (_gate)
        {
            if (!_registrations.Remove(type, out Registration? registration))
            {
                return false;
            }

            registration.Release();

            return true;
        }
    }

    /// <summary>
    /// Releases every registration, putting the compiled markup back in charge.
    /// </summary>
    /// <remarks>
    /// Required, not optional, when the registered types live in a collectible load context: the
    /// override field on a type roots the delegate, the delegate roots this service, and this
    /// service roots the documents — so an undisposed registry is a load context that never
    /// collects. Dispose this before disposing whatever owns the assemblies.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (Registration registration in _registrations.Values)
            {
                registration.Release();
            }

            _registrations.Clear();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// The two generated members population stands on, or nothing when the type has no compiled
    /// markup.
    /// </summary>
    /// <remarks>
    /// Both are checked for shape as well as name. A field of another type, whatever it is called,
    /// is not the seam this rides on — installing into it would fail at invocation time, far from
    /// the mistake.
    /// </remarks>
    private static (FieldInfo Field, MethodInfo Trampoline)? Hooks(Type type)
    {
        FieldInfo? field = type.GetField(
            OverrideFieldName, BindingFlags.Static | BindingFlags.NonPublic);

        MethodInfo? trampoline = type.GetMethod(
            TrampolineMethodName, BindingFlags.Static | BindingFlags.NonPublic);

        if (field is null
            || trampoline is null
            || field.FieldType != typeof(Action<object>)
            || trampoline.GetParameters() is not [{ ParameterType: { } parameter }]
            || !parameter.IsAssignableFrom(type))
        {
            return null;
        }

        return (field, trampoline);
    }

    /// <summary>
    /// Populates one instance from the prepared document, on the constructing thread.
    /// </summary>
    private void Populate(Registration registration, object instance)
    {
        // Cleared by removal between the trampoline reading the field and the delegate running,
        // or between preparation and the swap. The compiled markup is the honest answer for both.
        if (Volatile.Read(ref registration.Prepared) is not { } prepared || registration.Removed)
        {
            registration.PopulateFromCompiledMarkup(instance);

            return;
        }

        HashSet<Type> populating = t_populating ??= [];

        if (!populating.Add(registration.Type))
        {
            // This type is already being populated further up this same stack, so its live
            // document — directly or through others — places the control inside itself.
            // Construction has to bottom out somewhere, and the compiled markup is the one
            // version of this control known not to contain it.
            registration.PopulateFromCompiledMarkup(instance);

            Report(registration, prepared, MarkupDiagnostic.Load(
                XamlLoaderDiagnosticCodes.LivePopulationCycle,
                $"'{registration.Type.FullName}' places itself, through its own live document or " +
                "a cycle of documents placing each other. The inner instance shows the compiled markup.",
                MarkupDiagnosticSeverity.Warning,
                prepared.Document.Uri));

            return;
        }

        try
        {
            var diagnostics = new List<MarkupDiagnostic>();

            try
            {
                // Inside the compilation scope for the same reason a session load is: the
                // compiled code this produces must bind the generation these types live in,
                // not whichever assemblies the process's compiler saw first.
                using (_environment.CompilationScope?.Enter())
                {
                    var configuration = new RuntimeXamlLoaderConfiguration
                    {
                        LocalAssembly = registration.Type.Assembly,
                        UseCompiledBindingsByDefault = _options.UseCompiledBindingsByDefault,

                        // Never design mode: an embedded instance must behave the way its
                        // compiled markup would, and d:DesignWidth applied here would size a
                        // control that is not being previewed on its own.
                        DesignMode = false,

                        // Source information still says which document built what, which is what
                        // keeps an outer session's object map honest about objects that are not
                        // its document's to claim.
                        CreateSourceInfo = true,
                        DiagnosticHandler = diagnostic =>
                        {
                            if (diagnostic.Severity == RuntimeXamlDiagnosticSeverity.Warning)
                            {
                                diagnostics.Add(MarkupDiagnostic.Load(
                                    XamlLoaderDiagnosticCodes.LivePopulationFailed,
                                    $"{diagnostic.Id}: {diagnostic.Title}",
                                    MarkupDiagnosticSeverity.Warning,
                                    prepared.Document.Uri));
                            }

                            return diagnostic.Severity;
                        },
                    };

                    AvaloniaRuntimeXamlLoader.Load(
                        new RuntimeXamlLoaderDocument(prepared.BaseUri, instance, prepared.Text),
                        configuration);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A document mid-edit fails to compile as a matter of routine, and this failure
                // surfaces inside a constructor somebody else is running. The compiled markup is
                // what the instance would have shown had nothing been registered, so it is what
                // the instance shows now — stale beats blank, and the event says which it was.
                registration.PopulateFromCompiledMarkup(instance);

                diagnostics.Add(MarkupDiagnostic.Load(
                    XamlLoaderDiagnosticCodes.LivePopulationFailed,
                    $"Populating '{registration.Type.FullName}' from its live document failed: " +
                    $"{error.Message} The instance shows the compiled markup.",
                    MarkupDiagnosticSeverity.Error,
                    prepared.Document.Uri));

                Report(registration, prepared, [.. diagnostics]);
            }
        }
        finally
        {
            populating.Remove(registration.Type);
        }
    }

    private void Report(Registration registration, PreparedDocument prepared, MarkupDiagnostic diagnostic) =>
        Report(registration, prepared, ImmutableArray.Create(diagnostic));

    /// <summary>Raises <see cref="PopulationFailed"/>, isolating each subscriber.</summary>
    private void Report(
        Registration registration, PreparedDocument prepared, ImmutableArray<MarkupDiagnostic> diagnostics)
    {
        if (PopulationFailed is not { } handlers)
        {
            return;
        }

        var args = new XamlLivePopulationFailedEventArgs(registration.Type, prepared.Document, diagnostics);

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<XamlLivePopulationFailedEventArgs>)handler)(this, args);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A subscriber's failure is its own. This is running inside a constructor, and
                // letting a logging handler break the control it was logging about would be a
                // strange trade.
            }
        }
    }

    /// <summary>The text a document was projected to, ready to hand to the runtime loader.</summary>
    private sealed record PreparedDocument(XamlDocument Document, Uri? BaseUri, string Text);

    /// <summary>One overridden type: its hooks, its delegate, and the document it populates from.</summary>
    private sealed class Registration
    {
        private readonly FieldInfo _field;
        private readonly MethodInfo _trampoline;

        /// <summary>The prepared document, replaced whole on every registration. Read volatile.</summary>
        public PreparedDocument? Prepared;

        public Registration(
            XamlLivePopulation owner,
            Type type,
            FieldInfo field,
            MethodInfo trampoline,
            PreparedDocument prepared)
        {
            Type = type;
            Prepared = prepared;

            _field = field;
            _trampoline = trampoline;

            Override = instance => owner.Populate(this, instance);
        }

        public Type Type { get; }

        /// <summary>The delegate installed into the override field.</summary>
        public Action<object> Override { get; }

        /// <summary>Whether this registration has been released. Written before the field is cleared.</summary>
        public bool Removed { get; private set; }

        /// <summary>
        /// Puts the compiled markup back in charge, unless somebody else has since claimed the field.
        /// </summary>
        public void Release()
        {
            Removed = true;

            if (ReferenceEquals(_field.GetValue(null), Override))
            {
                _field.SetValue(null, null);
            }
        }

        /// <summary>
        /// Runs the compiled populate over an instance, through the generated trampoline.
        /// </summary>
        /// <remarks>
        /// The override field is cleared for the length of the call, because the trampoline is
        /// the one caller that knows how to build the service provider the compiled markup
        /// expects, and it consults the field first. Population happens on the instance's own
        /// thread; a second thread constructing the same type inside this window gets the
        /// compiled markup too, which is the fallback's own behaviour and not a corruption.
        /// </remarks>
        public void PopulateFromCompiledMarkup(object instance)
        {
            _field.SetValue(null, null);

            try
            {
                _trampoline.Invoke(null, [instance]);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
            {
                // The compiled populate's own failure is the caller's to see, exactly as it
                // would have been with nothing registered.
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
            }
            finally
            {
                if (!Removed)
                {
                    _field.SetValue(null, Override);
                }
            }
        }
    }
}
