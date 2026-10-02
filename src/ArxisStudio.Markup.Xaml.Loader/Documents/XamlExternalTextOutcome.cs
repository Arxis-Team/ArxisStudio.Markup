namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a <see cref="XamlLiveDocument"/> did with text that arrived from outside.</summary>
public enum XamlExternalTextOutcome
{
    /// <summary>The text is the document's own. It is now what is saved, and nothing else moved.</summary>
    AlreadyCurrent,

    /// <summary>The text became the document's, as one step of its history, and is what is saved.</summary>
    Taken,

    /// <summary>
    /// The document holds unsaved changes and the policy said not to overwrite them. Nothing moved.
    /// </summary>
    Conflict,

    /// <summary>The document kept its text, and now reads as changed against what is saved.</summary>
    KeptMine,
}
