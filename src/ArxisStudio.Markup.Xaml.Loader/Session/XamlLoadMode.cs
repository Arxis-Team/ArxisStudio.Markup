namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>Whether a document is being loaded to run or to be looked at.</summary>
public enum XamlLoadMode
{
    /// <summary>Load as the application would: design-only values are ignored.</summary>
    Runtime,

    /// <summary>
    /// Load for inspection: design-time values apply, so a document full of bindings still shows
    /// something.
    /// </summary>
    Design,
}
