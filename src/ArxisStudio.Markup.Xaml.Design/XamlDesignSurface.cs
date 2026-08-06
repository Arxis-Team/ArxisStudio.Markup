using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
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
    /// <exception cref="InvalidOperationException">Called from a thread that may not touch the session.</exception>
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
                IsTopLevel = true;
                Borrow(top);
                Project(top);
                break;

            case Control control:
                _scope.Child = control;
                break;
        }

        HasContent = _scope.Child is not null;
    }

    /// <summary>Lets go of the attached root, giving back anything taken from it.</summary>
    /// <remarks>Detaching twice is not an error, and detaching when nothing is attached does nothing.</remarks>
    public void Detach()
    {
        foreach (IDisposable mirror in _mirrors)
        {
            mirror.Dispose();
        }

        _mirrors.Clear();

        _scope.Child = null;

        Return();

        Root = null;
        IsTopLevel = false;
        HasContent = false;
    }

    /// <summary>Detaches, and refuses to attach again.</summary>
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

        _borrowed = top.Content;
        top.Content = null;
        _scope.Child = _borrowed as Control;

        _borrowedResources = top.Resources;
        top.Resources = new ResourceDictionary();
        Resources = _borrowedResources;

        _borrowedStyles.AddRange(top.Styles);
        top.Styles.Clear();

        foreach (IStyle style in _borrowedStyles)
        {
            Styles.Add(style);
        }
    }

    /// <summary>Gives all of it back, in the reverse order it was taken.</summary>
    private void Return()
    {
        if (_donor is null)
        {
            return;
        }

        // Released here before being added there: the collection an owner is being taken from has
        // to let go first, which is the whole reason any of this is a move rather than a share.
        Styles.Clear();

        foreach (IStyle style in _borrowedStyles)
        {
            _donor.Styles.Add(style);
        }

        _borrowedStyles.Clear();

        if (_borrowedResources is not null)
        {
            Resources = new ResourceDictionary();
            _donor.Resources = _borrowedResources;
            _borrowedResources = null;
        }

        _donor.Content = _borrowed;

        _donor = null;
        _borrowed = null;
    }

    /// <summary>Binds and shares everything the content would have inherited from the root.</summary>
    private void Project(TopLevel top)
    {
        _mirrors.Add(this.Bind(BackgroundProperty, top.GetObservable(TemplatedControl.BackgroundProperty)));
        _mirrors.Add(this.Bind(WidthProperty, top.GetObservable(WidthProperty)));
        _mirrors.Add(this.Bind(HeightProperty, top.GetObservable(HeightProperty)));

        _mirrors.Add(_scope.Bind(
            ThemeVariantScope.RequestedThemeVariantProperty,
            top.GetObservable(TopLevel.RequestedThemeVariantProperty)));

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
