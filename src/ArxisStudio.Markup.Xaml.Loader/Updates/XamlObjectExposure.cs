namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Whether a write lands on an object the session has handed out, or on one only this update has
/// ever seen.
/// </summary>
/// <remarks>
/// The difference decides what a throwing setter costs. A setter is free to assign its field,
/// raise a notification, touch a second property and then throw, so once one has been invoked
/// nothing can prove the object is unchanged — and comparing the property afterwards proves
/// nothing about the rest of it. On an object the caller already holds, that is a session which
/// no longer describes any document. On a copy this update built and is about to discard, it is
/// nothing at all: whatever the setter did goes out with the copy.
/// </remarks>
internal enum XamlObjectExposure
{
    /// <summary>An object the session has handed out and a caller may be holding.</summary>
    Live,

    /// <summary>A copy built by this update, which nothing outside it has seen.</summary>
    Rebuilt,
}
