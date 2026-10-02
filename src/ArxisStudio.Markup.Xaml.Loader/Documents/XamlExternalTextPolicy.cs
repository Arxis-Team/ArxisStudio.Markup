namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a <see cref="XamlLiveDocument"/> does with text that arrived from outside — a file written
/// by another editor — while it may hold changes of its own that nothing has saved.
/// </summary>
public enum XamlExternalTextPolicy
{
    /// <summary>
    /// Takes the text when nothing is unsaved, and otherwise changes nothing and reports a conflict
    /// for the host to put to the user. Never overwrites unsaved work silently.
    /// </summary>
    ApplyIfClean,

    /// <summary>
    /// Takes the text whatever is unsaved. What was unsaved is one undo away: the text arrives as a
    /// step of the history like any edit.
    /// </summary>
    TakeTheirs,

    /// <summary>
    /// Keeps the document's text and records the outside text as what is saved, so the document
    /// reads as changed — and the next save writes the document's text over it.
    /// </summary>
    KeepMine,
}
