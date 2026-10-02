namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What reading a binding path against a source type found.</summary>
public enum XamlBindingPathStatus
{
    /// <summary>Every step of the path is a member of the type before it.</summary>
    Resolved,

    /// <summary>
    /// A step names nothing on the type before it — a member renamed or removed in the code — so the
    /// binding shows nothing, and where bindings compile, the document does not load.
    /// </summary>
    Broken,

    /// <summary>
    /// The path says something this reading does not follow — an attached property, a cast, an
    /// element or ancestor source — or a step's type is only known when the binding runs. Nothing is
    /// claimed about it either way.
    /// </summary>
    NotUnderstood,
}
