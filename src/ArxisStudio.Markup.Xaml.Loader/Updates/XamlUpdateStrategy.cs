namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// How much has to be rebuilt to bring the objects in line with a changed document.
/// </summary>
/// <remarks>
/// <para>
/// In increasing order of what it costs and of how much it disturbs. An update takes the
/// smallest strategy that is certainly enough, and where a change could plausibly need either of
/// two, it takes the larger: an update that quietly does too little leaves the objects saying
/// something the document does not, which is the failure this whole library exists to prevent.
/// </para>
/// <para>
/// Comparison is over the syntax tree, not the text, so a comment, a blank line or a
/// reindentation is not a change at all and needs nothing.
/// </para>
/// </remarks>
public enum XamlUpdateStrategy
{
    /// <summary>Nothing that affects an object changed.</summary>
    None,

    /// <summary>A literal value on a writable member; the property is set where it stands.</summary>
    SetProperty,

    /// <summary>A design-time value; the design value is updated, and only in design mode.</summary>
    UpdateDesignValue,

    /// <summary>A value in a resource dictionary; the entry is replaced.</summary>
    ReplaceResource,

    /// <summary>Something inside a style; the style is rebuilt and put back where it was.</summary>
    ReloadStyle,

    /// <summary>Something inside a control theme; the theme is rebuilt and put back.</summary>
    ReloadTheme,

    /// <summary>Something inside a control template; the template is rebuilt and its content recreated.</summary>
    ReloadTemplate,

    /// <summary>The shape of the tree changed; the affected element's objects are rebuilt.</summary>
    ReloadSubtree,

    /// <summary>
    /// The root element or <c>x:Class</c> changed, which no in-place update can express: the
    /// caller has to create a new session.
    /// </summary>
    RecreateSession,
}
