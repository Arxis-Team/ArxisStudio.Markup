namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a <see cref="XamlLiveDocument"/> did with text that arrived from outside.</summary>
public enum XamlExternalTextOutcome
{
    /// <summary>The text is the document's own. It is now what is saved, and nothing else moved.</summary>
    AlreadyCurrent,

    /// <summary>
    /// The text is what is saved — the document's last save or read, reported late by whatever
    /// watches the file — so nothing changed where it is saved, and nothing moved. Answered only to
    /// <see cref="XamlExternalTextPolicy.ApplyIfClean"/>: a host that asks to take or keep a text has
    /// made a choice, and it is honoured.
    /// </summary>
    AlreadySaved,

    /// <summary>The text became the document's, as one step of its history, and is what is saved.</summary>
    Taken,

    /// <summary>
    /// The document holds unsaved changes and the policy said not to overwrite them. Nothing moved.
    /// </summary>
    Conflict,

    /// <summary>The document kept its text, and now reads as changed against what is saved.</summary>
    KeptMine,
}
