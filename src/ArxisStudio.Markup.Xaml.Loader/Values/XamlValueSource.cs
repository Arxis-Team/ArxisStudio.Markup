namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Where a property's current value came from.
/// </summary>
/// <remarks>
/// This is the distinction the whole project turns on. A property showing "Alice" because a
/// binding produced it is not a property set to "Alice", and writing the document as though it
/// were would replace the author's expression with today's data.
/// </remarks>
public enum XamlValueSource
{
    /// <summary>Nothing has set the property; it is showing its default.</summary>
    Unset,

    /// <summary>Set directly on the object, which is what a literal in the document produces.</summary>
    Local,

    /// <summary>Produced by a binding, which owns the property until the binding is removed.</summary>
    Binding,

    /// <summary>Provided by a style or a control theme.</summary>
    Style,

    /// <summary>Provided by a style trigger.</summary>
    StyleTrigger,

    /// <summary>Provided by the template the object was created from.</summary>
    Template,

    /// <summary>Inherited from an ancestor.</summary>
    Inherited,

    /// <summary>Being driven by an animation.</summary>
    Animation,
}
