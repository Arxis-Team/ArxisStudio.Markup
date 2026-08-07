using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Threading;
using ArxisStudio.Markup.Xaml.Loader;

namespace ArxisStudio.Markup.Xaml.Design;

/// <summary>
/// Stands in for a loaded root that cannot be shown, so that a design surface can host it.
/// </summary>
/// <remarks>
/// <para>
/// The commonest document in any Avalonia application is a window, and a <see cref="Window"/> is a
/// <see cref="TopLevel"/>: Avalonia parents it at construction, so making it the content of anything
/// throws during layout. The object the loader correctly produces for <c>MainWindow.axaml</c> is
/// therefore an object nothing can display, and every host that shows forms has had to invent the
/// same answer. This is that answer, written once.
/// </para>
/// <para>
/// <b>It projects rather than copies.</b> Background, size and theme variant are bound, so a
/// property edited through the session shows immediately — without rebuilding anything, and so
/// without losing focus or scroll position inside the form. A snapshot would answer the first
/// question anyone asks — what happens when the inspector edits a property of the window itself —
/// with "nothing visible".
/// </para>
/// <para>
/// <b>Resources and styles are borrowed, not copied and not shared.</b> Avalonia allows a resource
/// dictionary one owner and refuses a second — "The ResourceDictionary already has a parent" — and
/// it seals the interfaces that would let a forwarder stand between them. So the root's dictionary
/// and its styles are moved onto the stand-in for as long as it is attached and given back on
/// <see cref="Detach"/>. Moving rather than copying is what keeps merged dictionaries and theme
/// dictionaries intact: they are the same objects, not a flattened snapshot of the entries a copy
/// would have reached.
/// </para>
/// <para>
/// <b>It never writes back.</b> Every mirror is one-way. The document is the truth; a resize or any
/// other gesture edits the document through the session and arrives here as a new value. Two writers
/// for one value is how this kind of tool acquires a feedback loop in which the form shivers on every
/// frame of a drag.
/// </para>
/// <para>
/// <b>The root stays the root.</b> <see cref="XamlLoadSession.RootObject"/> is untouched, so the
/// object map, <c>x:Class</c>, member resolution and every edit path work exactly as they do without
/// this. Only the presentation gets a stand-in.
/// </para>
/// <para>
/// One category cannot be projected, because there is no title bar here to project it onto:
/// <see cref="Title"/>, <see cref="Icon"/>, <see cref="CanResize"/> and <see cref="Decorations"/> are
/// properties of a window <em>as a window</em>. They are published as data and a host draws its own
/// chrome from them. <c>WindowState</c> is deliberately absent: a window that is never shown is
/// always in its normal state, so surfacing it would be a promise with nothing behind it.
/// </para>
/// <para>
/// This type hosts; it does not select. There is no adorner, no handle, no pointer or keyboard
/// handling and no inspector here, and there must not be — see
/// <c>docs/adr/0012-hosting-a-top-level-root-is-a-package-beside-the-loader.md</c>.
/// </para>
/// </remarks>
public sealed class XamlDesignSurface : Border, IDisposable
{
    /// <summary>Defines the <see cref="Root"/> property.</summary>
    public static readonly DirectProperty<XamlDesignSurface, object?> RootProperty =
        AvaloniaProperty.RegisterDirect<XamlDesignSurface, object?>(
            nameof(Root), static surface => surface.Root);

    /// <summary>Defines the <see cref="IsTopLevel"/> property.</summary>
    public static readonly DirectProperty<XamlDesignSurface, bool> IsTopLevelProperty =
        AvaloniaProperty.RegisterDirect<XamlDesignSurface, bool>(
            nameof(IsTopLevel), static surface => surface.IsTopLevel);

    /// <summary>Defines the <see cref="HasContent"/> property.</summary>
    public static readonly DirectProperty<XamlDesignSurface, bool> HasContentProperty =
        AvaloniaProperty.RegisterDirect<XamlDesignSurface, bool>(
            nameof(HasContent), static surface => surface.HasContent);

    /// <summary>Defines the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<XamlDesignSurface, string?>(nameof(Title));

    /// <summary>Defines the <see cref="Icon"/> property.</summary>
    public static readonly StyledProperty<WindowIcon?> IconProperty =
        AvaloniaProperty.Register<XamlDesignSurface, WindowIcon?>(nameof(Icon));

    /// <summary>Defines the <see cref="CanResize"/> property.</summary>
    public static readonly StyledProperty<bool> CanResizeProperty =
        AvaloniaProperty.Register<XamlDesignSurface, bool>(nameof(CanResize), defaultValue: true);

    /// <summary>Defines the <see cref="Decorations"/> property.</summary>
    public static readonly StyledProperty<WindowDecorations> DecorationsProperty =
        AvaloniaProperty.Register<XamlDesignSurface, WindowDecorations>(
            nameof(Decorations), defaultValue: WindowDecorations.Full);

    /// <summary>
    /// Carries the root's requested theme variant down to the content.
    /// </summary>
    /// <remarks>
    /// A variant is a scope rather than a value, and the property that declares one lives on
    /// <see cref="TopLevel"/> — which is exactly the thing not in the tree. So the content is hosted
    /// inside a scope of its own and the root's request is bound to it. Without this, a form that
    /// asks for a light variant inside a dark tool is shown dark.
    /// </remarks>
    private readonly ThemeVariantScope _scope = new();

    private readonly List<IDisposable> _mirrors = [];

    private object? _root;
    private bool _isTopLevel;
    private bool _hasContent;

    /// <summary>The root whose content was taken, and which it must be given back to.</summary>
    private TopLevel? _donor;

    private object? _borrowed;
    private IResourceDictionary? _borrowedResources;
    private readonly List<IStyle> _borrowedStyles = [];
    private bool _disposed;

    /// <summary>
    /// Which surface is standing in for which root.
    /// </summary>
    /// <remarks>
    /// One surface owns a root at a time, and the price of not enforcing it is silent: the second
    /// surface borrows what the first left behind — a null content and an empty dictionary — and
    /// records itself as a donor anyway, so whichever detaches last writes those substitutes back
    /// and empties the window. The first detach looks like it worked, which is what makes it a
    /// trap rather than an error.
    /// </remarks>
    private static readonly ConditionalWeakTable<TopLevel, XamlDesignSurface> StandingIn = [];

    /// <summary>Creates a surface with nothing attached to it.</summary>
    public XamlDesignSurface() => Child = _scope;

    /// <summary>The object the attached session produced, or <see langword="null"/> when detached.</summary>
    /// <remarks>
    /// This is the real root — the window itself, not the stand-in — because it is what edits
    /// address. A host that needs to inspect or edit the form's own properties uses this.
    /// </remarks>
    public object? Root
    {
        get => _root;
        private set => SetAndRaise(RootProperty, ref _root, value);
    }

    /// <summary>Whether the attached root is a <see cref="TopLevel"/> and is being stood in for.</summary>
    /// <remarks>
    /// <see langword="false"/> means the root was hostable as it stood and is simply being held; the
    /// chrome properties then say nothing, and nothing is projected, because a root that is really in
    /// the tree already carries its own resources, styles and variant.
    /// </remarks>
    public bool IsTopLevel
    {
        get => _isTopLevel;
        private set => SetAndRaise(IsTopLevelProperty, ref _isTopLevel, value);
    }

    /// <summary>Whether there is anything to show.</summary>
    /// <remarks>
    /// A document need not produce a control at all — <c>App.axaml</c> produces an application, a
    /// resource dictionary produces a dictionary — and a designer that opens whatever the user
    /// clicked will meet those. It is a thing to report, not a thing to fail on.
    /// </remarks>
    public bool HasContent
    {
        get => _hasContent;
        private set => SetAndRaise(HasContentProperty, ref _hasContent, value);
    }

    /// <summary>The window's title, for a host drawing its own chrome.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        private set => SetValue(TitleProperty, value);
    }

    /// <summary>The window's icon, for a host drawing its own chrome.</summary>
    public WindowIcon? Icon
    {
        get => GetValue(IconProperty);
        private set => SetValue(IconProperty, value);
    }

    /// <summary>Whether the window declares itself resizable.</summary>
    public bool CanResize
    {
        get => GetValue(CanResizeProperty);
        private set => SetValue(CanResizeProperty, value);
    }

    /// <summary>What decorations the window asks the system for.</summary>
    public WindowDecorations Decorations
    {
        get => GetValue(DecorationsProperty);
        private set => SetValue(DecorationsProperty, value);
    }

    /// <summary>Stands in for the root of <paramref name="session"/>, replacing whatever was attached.</summary>
    /// <remarks>
    /// <para>
    /// Call this again after an update that rebuilt the root. It is not a rebuild: the surface is the
    /// same control, so a host holding it in a canvas keeps its place, its size and its selection.
    /// </para>
    /// <para>
    /// Taking a top-level's content means taking it — one control cannot be in two logical trees,
    /// and one resource dictionary cannot have two owners — so the content, the resources and the
    /// styles are moved across and given back on <see cref="Detach"/>. One surface owns a root at a
    /// time. While it is held, the root reports none of the three; the document is unchanged and
    /// still says what it says, which is what every edit path reads.
    /// </para>
    /// </remarks>
    /// <param name="session">The session whose root to stand in for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This surface has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called from a thread that may not touch the session, or another surface is already standing
    /// in for this root.
    /// </exception>
    public void Attach(XamlLoadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The objects about to be read and re-parented are Avalonia objects the session owns, and
        // asking it is how the same rule gets applied here as everywhere else that touches them.
        session.VerifyAccess();

        Detach();

        Root = session.RootObject;

        switch (Root)
        {
            case TopLevel top:
                if (StandingIn.TryGetValue(top, out XamlDesignSurface? already)
                    && !ReferenceEquals(already, this))
                {
                    Root = null;

                    throw new InvalidOperationException(
                        "Another XamlDesignSurface is already standing in for this root. "
                        + "One surface owns a root at a time; detach the other one first.");
                }

                IsTopLevel = true;
                Borrow(top);
                Project(top);
                break;

            case Control control:
                // Nothing is projected for a root that was hostable as it stood -- it carries its
                // own resources, styles and variant -- but the insulation still applies: a local
                // null blocks inheritance, so the host's own view model cannot arrive as this
                // form's data through a template that happens to be bound to one.
                _scope.DataContext = null;
                _scope.Child = control;
                break;
        }

        HasContent = _scope.Child is not null;
    }

    /// <summary>Lets go of the attached root, giving back anything taken from it.</summary>
    /// <remarks>
    /// <para>Detaching twice is not an error, and detaching when nothing is attached does nothing.</para>
    /// <para>
    /// Checked for thread access like <see cref="Attach"/>, and for the same reason: giving a root
    /// its content, resources and styles back is mutating the same Avalonia objects that taking
    /// them was. The dispatcher is asked rather than the session, because a host may well have
    /// disposed the session first — that is the ordinary teardown order — and a disposed session
    /// answers a thread question with <see cref="ObjectDisposedException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called from a thread that may not touch these objects.</exception>
    public void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();

        foreach (IDisposable mirror in _mirrors)
        {
            mirror.Dispose();
        }

        _mirrors.Clear();

        _scope.Child = null;

        Return();

        ClearValue(BackgroundProperty);

        Root = null;
        IsTopLevel = false;
        HasContent = false;
    }

    /// <summary>Detaches, and refuses to attach again.</summary>
    /// <exception cref="InvalidOperationException">Called from a thread that may not touch these objects.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();

        _disposed = true;
    }

    /// <summary>Takes what Avalonia will not let two elements have at once.</summary>
    private void Borrow(TopLevel top)
    {
        _donor = top;

        StandingIn.Add(top, this);

        _borrowed = top.Content;
        top.Content = null;

        // A window's content need not be a control: a string, or a view model the window resolves
        // with its own ContentTemplate, is content that renders perfectly well outside a designer.
        // Dropping it would draw a blank card that nothing could tell apart from an empty form, so
        // it is presented the way the window would have presented it.
        _scope.Child = _borrowed switch
        {
            Control control => control,
            null => null,
            _ => new ContentControl { Content = _borrowed, ContentTemplate = top.ContentTemplate },
        };

        // Merged into ours rather than put in its place. Avalonia refuses a second owner, which is
        // why the root has to let go first — but once it has, the dictionary can be merged, and then
        // the host's own resources stay reachable the whole time instead of being set aside. A key
        // both declare resolves to the host's, which is the right way round: the host's are the
        // frame around the form, not something the form may quietly redefine.
        _borrowedResources = top.Resources;
        top.Resources = new ResourceDictionary();
        Resources.MergedDictionaries.Add(_borrowedResources);

        _borrowedStyles.AddRange(top.Styles);
        top.Styles.Clear();

        foreach (IStyle style in _borrowedStyles)
        {
            Styles.Add(style);
        }
    }

    /// <summary>
    /// Shows the root's background when the document gave it one, and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// Where the value came from is the whole question, and <c>IsSet</c> does not answer it — it is
    /// true for a themed value as much as for a declared one. The priority does answer it, and
    /// reading a priority is what <c>GetDiagnostic</c> is for. Anything weaker than a local value
    /// was supplied by the application the designer itself is running under, and showing it would
    /// paint every undecided form in the tool's own colour while claiming it was the form's.
    /// </remarks>
    /// <remarks>
    /// One transition cannot be seen from here, and it is worth naming rather than pretending
    /// otherwise: a change of priority that leaves the effective value untouched — a document
    /// declaring locally the very brush instance a theme was already supplying, or clearing one —
    /// raises no property-changed notification, so this is not re-run. Avalonia offers no
    /// observable of a value's priority, only the one-shot <c>GetDiagnostic</c>, so there is
    /// nothing to subscribe to. Re-attaching re-reads it. Recorded in <c>docs/limitations.md</c>.
    /// </remarks>
    private void ShowBackground(TopLevel top)
    {
        AvaloniaPropertyValue declared = top.GetDiagnostic(TemplatedControl.BackgroundProperty);

        Background = declared.Priority <= BindingPriority.LocalValue ? declared.Value as IBrush : null;
    }

    /// <summary>Gives all of it back, in the reverse order it was taken.</summary>
    private void Return()
    {
        if (_donor is null)
        {
            return;
        }

        // Exactly the borrowed ones, and released here before being added there: the collection an
        // owner is being taken from has to let go first, which is the whole reason any of this is a
        // move rather than a share. Clearing the collection would take the host's own styles with
        // them, and nothing would ever put those back.
        foreach (IStyle style in _borrowedStyles)
        {
            Styles.Remove(style);
            _donor.Styles.Add(style);
        }

        _borrowedStyles.Clear();

        if (_borrowedResources is not null)
        {
            Resources.MergedDictionaries.Remove(_borrowedResources);
            _donor.Resources = _borrowedResources;

            _borrowedResources = null;
        }

        _donor.Content = _borrowed;

        StandingIn.Remove(_donor);

        _donor = null;
        _borrowed = null;
    }

    /// <summary>Binds and shares everything the content would have inherited from the root.</summary>
    private void Project(TopLevel top)
    {
        // Not bound straight through, because a root that declares no background still has one:
        // the application the designer is itself running under supplies a themed default, and
        // painting that would show every undecided form in the tool's own colour while claiming it
        // was the form's. Only a value the document set is a value to show, and a host's own card
        // shows through when there is none.
        _mirrors.Add(top.GetPropertyChangedObservable(TemplatedControl.BackgroundProperty)
            .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => ShowBackground(top))));

        ShowBackground(top);
        _mirrors.Add(this.Bind(WidthProperty, top.GetObservable(WidthProperty)));
        _mirrors.Add(this.Bind(HeightProperty, top.GetObservable(HeightProperty)));

        _mirrors.Add(_scope.Bind(
            ThemeVariantScope.RequestedThemeVariantProperty,
            top.GetObservable(TopLevel.RequestedThemeVariantProperty)));

        // The one that is found last and looks like something else entirely. A form's design-time
        // data is set on its root, because `Design.DataContext` is a property of the window; taking
        // the content out of the window takes it out of that context too. Without this the bindings
        // under it go blank and the form measures to nothing, which looks exactly like a form that
        // failed to load -- and worse, whatever the host was bound to arrives in its place.
        _mirrors.Add(_scope.Bind(DataContextProperty, top.GetObservable(DataContextProperty)));

        if (top is Window window)
        {
            _mirrors.Add(this.Bind(TitleProperty, window.GetObservable(Window.TitleProperty)));
            _mirrors.Add(this.Bind(IconProperty, window.GetObservable(Window.IconProperty)));
            _mirrors.Add(this.Bind(CanResizeProperty, window.GetObservable(Window.CanResizeProperty)));
            _mirrors.Add(this.Bind(
                DecorationsProperty, window.GetObservable(Window.WindowDecorationsProperty)));
        }
    }
}
