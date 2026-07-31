namespace ArxisStudio.Markup.Xaml;

/// <summary>How a document should be written out.</summary>
public enum XamlWriteMode
{
    /// <summary>
    /// Keep every unchanged region exactly as it was, writing only what changed.
    /// </summary>
    /// <remarks>
    /// The default everywhere, and the only mode a save ever needs. An unedited document
    /// written this way reproduces its source byte for byte.
    /// </remarks>
    Preserve,

    /// <summary>
    /// Reflow the whole document according to explicit formatting options.
    /// </summary>
    /// <remarks>
    /// Rewrites regions nobody edited, so it is never enabled implicitly. A caller has to ask
    /// for it by name, because asking for it means accepting a diff across the whole file.
    /// </remarks>
    Format,
}
