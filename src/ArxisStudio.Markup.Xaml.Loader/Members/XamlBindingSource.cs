namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a binding reads from, as its markup says.</summary>
public enum XamlBindingSource
{
    /// <summary>The data context — what a binding reads unless it names something else.</summary>
    DataContext,

    /// <summary>An element of the document by name: <c>#title.Text</c>, or <c>ElementName</c>.</summary>
    Element,

    /// <summary>The element the binding is written on: <c>$self</c>, or <c>RelativeSource Self</c>.</summary>
    Self,

    /// <summary>An ancestor: <c>$parent[Window]</c>, or <c>RelativeSource FindAncestor</c>.</summary>
    Ancestor,

    /// <summary>
    /// Something a reading from types does not follow: a <c>Source</c>, a templated parent, a mode it does
    /// not know.
    /// </summary>
    Other,
}
