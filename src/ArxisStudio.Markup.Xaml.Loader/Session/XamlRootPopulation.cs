using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Lends an <c>x:Class</c> root's constructor the session's document, for one construction.
/// </summary>
/// <remarks>
/// <para>
/// A generated <c>x:Class</c> partial loads its own markup from its constructor. Constructing
/// one and then populating it therefore loaded markup twice — the markup compiled into the
/// assembly first, the document after it — and everything the root <em>accumulates</em> rather
/// than assigns came out doubled: a second copy of each style, each handler attached twice, and
/// a keyed resource added under a key the dictionary already held, which throws and costs the
/// whole load. What the constructor kept for itself was worse than doubled: the fields a
/// generated <c>InitializeComponent</c> reads out of the name scope pointed at controls the
/// second population then replaced.
/// </para>
/// <para>
/// So the document is handed to the constructor's own load instead. Avalonia's XAML compiler
/// emits, into every <c>x:Class</c> type, a hook its generated load consults before the compiled
/// markup (<see cref="XamlPopulateHook"/>); while the root is being constructed the hook is this
/// object, and the first instance of the type made on this thread is populated by the session —
/// its projection, its mode, its diagnostics. There is one population, it is the document's, and
/// the constructor's own code runs against the tree that will be shown. It is what Avalonia's own
/// runtime loader does when it is asked to create such a root itself; ADR 0015 records why the
/// session does it rather than delegating.
/// </para>
/// <para>
/// Nothing is detected and nothing is guessed. A constructor that loads nothing never calls the
/// hook, a type with no compiled markup has none, and a caller's factory that hands over an
/// instance made some other way has not constructed anything here — in all three
/// <see cref="Instance"/> stays <see langword="null"/> and the session populates the instance
/// afterwards, exactly as it always has.
/// </para>
/// <para>
/// The hook is process-wide state on the type, so it is borrowed rather than taken: what was
/// installed before — a <see cref="XamlLivePopulation"/> registration for the same type, which is
/// the ordinary case in a designer — is put back on disposal, and answers in the meantime for
/// every instance that is not the root: a copy of the control the document places inside itself,
/// or one another thread happens to construct.
/// </para>
/// </remarks>
internal sealed class XamlRootPopulation : IDisposable
{
    private readonly XamlPopulateHook? _hook;
    private readonly Func<object, object?> _populate;
    private readonly Action<object>? _previous;
    private readonly Action<object>? _installed;
    private readonly int _thread;

    private bool _returned;

    private XamlRootPopulation(XamlPopulateHook? hook, Func<object, object?> populate)
    {
        _hook = hook;
        _populate = populate;
        _thread = System.Environment.CurrentManagedThreadId;

        if (hook is not null)
        {
            _previous = hook.Installed;
            _installed = Populate;

            hook.Installed = _installed;
        }
    }

    /// <summary>Gets the instance the constructor populated from the document, if it did.</summary>
    public object? Instance { get; private set; }

    /// <summary>
    /// Gets what populating <see cref="Instance"/> produced, which is nothing when the document
    /// would not build.
    /// </summary>
    public object? Root { get; private set; }

    /// <summary>
    /// Installs the session's population on a type for as long as the result is held.
    /// </summary>
    /// <remarks>
    /// Called on the thread that owns the objects, immediately before the root is constructed,
    /// and disposed immediately after: the window is one constructor long.
    /// </remarks>
    /// <param name="rootType">The type the document's <c>x:Class</c> names.</param>
    /// <param name="populate">
    /// Populates an instance from the document and returns the root it produced, or
    /// <see langword="null"/> when it could not. It reports rather than throws — it runs inside
    /// somebody else's constructor.
    /// </param>
    /// <returns>The loan, to be disposed once the instance has been constructed.</returns>
    public static XamlRootPopulation Lend(Type rootType, Func<object, object?> populate) =>
        new(XamlPopulateHook.Find(rootType), populate);

    /// <summary>Puts back what the type was populated by before.</summary>
    /// <remarks>
    /// Unless somebody else has claimed the hook since — a registration replaced while the root
    /// was being constructed is the newer truth and is left alone.
    /// </remarks>
    public void Dispose()
    {
        if (_returned)
        {
            return;
        }

        _returned = true;

        if (_hook is not null && ReferenceEquals(_hook.Installed, _installed))
        {
            _hook.Installed = _previous;
        }
    }

    private void Populate(object instance)
    {
        // One instance, on the thread that asked for it. Anything else constructed while the
        // hook is on loan is not the root, and gets what it would have got had nothing been lent.
        if (Instance is not null || System.Environment.CurrentManagedThreadId != _thread)
        {
            PassOn(instance);

            return;
        }

        Instance = instance;
        Root = _populate(instance);
    }

    /// <summary>Populates an instance that is not the root, the way it would have been anyway.</summary>
    private void PassOn(object instance)
    {
        if (_previous is not null)
        {
            _previous(instance);

            return;
        }

        try
        {
            _hook!.PopulateFromCompiledMarkup(instance);
        }
        finally
        {
            if (!_returned)
            {
                _hook!.Installed = _installed;
            }
        }
    }
}
