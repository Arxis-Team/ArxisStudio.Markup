namespace ArxisStudio.Markup.Xaml;

/// <summary>What a piece of <see cref="XamlTrivia"/> is.</summary>
public enum XamlTriviaKind
{
    /// <summary>Spaces and tabs.</summary>
    Whitespace,

    /// <summary>A line break, kept exactly as written.</summary>
    NewLine,

    /// <summary>Text the parser could not explain, retained so it can be written back.</summary>
    Skipped,
}
