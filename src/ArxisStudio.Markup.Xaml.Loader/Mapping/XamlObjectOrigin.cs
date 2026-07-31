namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>Where an object in a loaded tree came from.</summary>
/// <remarks>
/// The distinction decides what an edit is allowed to do. Only a <see cref="Document"/> object
/// has a declaration in this file to write back to; changing a template-generated one would
/// affect every control the template is applied to, which is almost never what a caller
/// selecting something on screen means.
/// </remarks>
public enum XamlObjectOrigin
{
    /// <summary>Declared by an element of the document being edited.</summary>
    Document,

    /// <summary>A value in a resource dictionary.</summary>
    Resource,

    /// <summary>Created by a style or a control theme.</summary>
    Style,

    /// <summary>Produced by applying a template.</summary>
    Template,

    /// <summary>Created at run time with no declaration behind it.</summary>
    RuntimeGenerated,
}
