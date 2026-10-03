namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a session does with the class a document's <c>x:Class</c> names.</summary>
/// <remarks>
/// A host decides this, not the document. The same <c>App.axaml</c> is an application to the program
/// it belongs to and a set of styles and resources to a designer showing that program's forms — and
/// constructing its class inside the designer would be the program's startup code running there.
/// </remarks>
public enum XamlClassUse
{
    /// <summary>
    /// The class is resolved, constructed and populated from the document: the root is what the
    /// program would build. A class that cannot be used is reported and left out (ADR 0017).
    /// </summary>
    Construct,

    /// <summary>
    /// The class is left out and the root is built as the element it is written as — the way a class
    /// the load cannot use is left out, without anything being reported about it, because nothing is
    /// wrong with it. A handler the document names has no instance to be hooked up to and is
    /// reported and left out, as it is for a document with no class.
    /// </summary>
    AsWritten,
}
