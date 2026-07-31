namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What kind of member a name in the document turned out to be.</summary>
/// <remarks>
/// This is the classification the syntax package deliberately refuses to make. It needs CLR
/// metadata, so it lives here.
/// </remarks>
public enum XamlMemberKind
{
    /// <summary>Nothing on the target type matched the name.</summary>
    Unknown,

    /// <summary>An Avalonia <c>StyledProperty</c>.</summary>
    StyledProperty,

    /// <summary>An Avalonia <c>DirectProperty</c>.</summary>
    DirectProperty,

    /// <summary>An Avalonia attached property, or an attached CLR accessor pair.</summary>
    AttachedProperty,

    /// <summary>An ordinary CLR property.</summary>
    ClrProperty,

    /// <summary>A routed or CLR event.</summary>
    Event,

    /// <summary>The type's content property, which unnamed child content goes into.</summary>
    Content,

    /// <summary>A member whose value is a collection that children are added to.</summary>
    Collection,
}
