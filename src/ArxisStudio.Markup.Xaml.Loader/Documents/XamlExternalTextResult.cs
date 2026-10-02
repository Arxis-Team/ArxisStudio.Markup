namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What a <see cref="XamlLiveDocument"/> did with text that arrived from outside.</summary>
public sealed class XamlExternalTextResult
{
    /// <summary>Gets what happened to the text.</summary>
    public required XamlExternalTextOutcome Outcome { get; init; }

    /// <summary>
    /// Gets what taking the text did to what shows the document, when it was taken; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public XamlLiveEditResult? Edit { get; init; }
}
