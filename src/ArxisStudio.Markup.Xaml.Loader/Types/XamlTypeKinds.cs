using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a type can be in a document — the questions a toolbox and a data panel ask before they
/// offer it.
/// </summary>
[Flags]
public enum XamlTypeKinds
{
    /// <summary>Nothing a document can do with it beyond naming it.</summary>
    None = 0,

    /// <summary>
    /// A document can write it as an element: a concrete, non-generic class with a public
    /// constructor that takes nothing.
    /// </summary>
    Creatable = 1,

    /// <summary>A control, which stands in a layout.</summary>
    Control = 2,

    /// <summary>A panel, which holds any number of controls.</summary>
    Panel = 4,

    /// <summary>A content control, which holds one thing — a control or a value.</summary>
    ContentControl = 8,

    /// <summary>A decorator, which holds one control.</summary>
    Decorator = 16,

    /// <summary>An items control, which shows a collection.</summary>
    ItemsControl = 32,

    /// <summary>A templated control, whose look is a template its theme sets.</summary>
    TemplatedControl = 64,

    /// <summary>A user control.</summary>
    UserControl = 128,

    /// <summary>A top level — a window — which is the root of a document and stands inside nothing.</summary>
    TopLevel = 256,

    /// <summary>
    /// A class with its own markup compiled into it — an <c>x:Class</c> of a project — which a
    /// session builds from that markup, live where a document of it is open.
    /// </summary>
    CompiledMarkup = 512,

    /// <summary>Not an Avalonia object: a model or a view model a document binds to.</summary>
    Data = 1024,
}
