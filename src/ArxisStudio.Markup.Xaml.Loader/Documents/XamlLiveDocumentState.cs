namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a <see cref="XamlLiveDocument"/>'s objects show of its text.
/// </summary>
/// <remarks>
/// The text is always the document's: an edit, an undo or text from outside lands in it whatever
/// the objects make of it. This is the other half — whether anything shows that text, and whether
/// what shows is the text as it reads now.
/// </remarks>
public enum XamlLiveDocumentState
{
    /// <summary>
    /// Nothing is loaded: the document has text and a history and no environment to build objects
    /// in — a tab nobody is looking at, or a document between two generations of a project's code.
    /// </summary>
    Detached,

    /// <summary>The session shows the document as it reads now.</summary>
    Live,

    /// <summary>
    /// The session shows an earlier text. The current one could not be shown — it does not parse,
    /// a type it names does not resolve, a value does not convert — and
    /// <see cref="XamlLiveDocument.Diagnostics"/> says why; the objects stay as the last text that
    /// could be shown left them.
    /// </summary>
    Behind,

    /// <summary>
    /// Nothing shows the document: it could not be loaded, and no earlier session is left that can
    /// be believed. <see cref="XamlLiveDocument.Diagnostics"/> says why.
    /// </summary>
    Broken,
}
