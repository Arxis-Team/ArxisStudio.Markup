namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a XAML document describes, judged by its root.</summary>
/// <remarks>
/// The root's own type decides, the most specific kind first: a <c>Window</c> is a control too,
/// and is a <see cref="Window"/> rather than a <see cref="Control"/>. Only a document whose root is
/// a set of styles or a resource dictionary is looked into further, because that is the one shape
/// whose purpose its root does not say — it may be a <see cref="TemplatedControl"/>.
/// </remarks>
public enum XamlDocumentKind
{
    /// <summary>
    /// The root's type could not be resolved, or the document has no root to judge. The
    /// classification's diagnostics say which.
    /// </summary>
    Unknown,

    /// <summary>An <c>Application</c> or a type derived from it — an application's <c>App.axaml</c>.</summary>
    Application,

    /// <summary>A <c>Window</c> or a type derived from it.</summary>
    Window,

    /// <summary>A <c>UserControl</c> or a type derived from it.</summary>
    UserControl,

    /// <summary>
    /// Any other control: a panel, a border, a button, or a control of the author's own that is
    /// none of the above.
    /// </summary>
    Control,

    /// <summary>
    /// The look of a templated control: styles or a resource dictionary that set the
    /// <c>Template</c> of a control written outside Avalonia's own namespace — the file a
    /// templated control is created with.
    /// </summary>
    TemplatedControl,

    /// <summary>A set of styles: a <c>Styles</c>, <c>Style</c> or <c>ControlTheme</c> root.</summary>
    Styles,

    /// <summary>A resource dictionary.</summary>
    ResourceDictionary,

    /// <summary>
    /// A root that is none of these — a brush, a data template, an object of the author's own.
    /// </summary>
    Other,
}
