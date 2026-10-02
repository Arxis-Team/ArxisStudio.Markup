using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Kinds;

/// <summary>
/// One file of the gallery, and what the classifier last said about it.
/// </summary>
/// <remarks>
/// The text is the file's until it is edited, and the edit's afterwards: the gallery classifies
/// what is typed, not what is on disk, and never writes anything back. What a row shows is the
/// classification's own vocabulary — the kind is the <see cref="XamlDocumentKind"/> name, not a
/// translation of it — so a reader can find each word in the API.
/// </remarks>
internal sealed class DocumentEntry(string fileName, Uri uri, string text) : INotifyPropertyChanged
{
    private XamlDocumentClassification? _classification;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the file's name.</summary>
    public string FileName { get; } = fileName;

    /// <summary>Gets the address the document is parsed under, which its diagnostics point at.</summary>
    internal Uri Uri { get; } = uri;

    /// <summary>Gets or sets the text being classified.</summary>
    internal string Text { get; set; } = text;

    /// <summary>Gets the last classification, or <see langword="null"/> before the first.</summary>
    internal XamlDocumentClassification? Classification => _classification;

    /// <summary>Gets the kind, as the API names it.</summary>
    public string Kind => _classification?.Kind.ToString() ?? "…";

    /// <summary>Gets what the kind rests on, in a line.</summary>
    public string Detail => _classification switch
    {
        null => "классифицируется",
        { Kind: XamlDocumentKind.Unknown } => "корень не разрешился",
        { } known =>
            (known.IsCustomRoot ? "корень проекта" : "корень Avalonia")
            + (known.IsResolved ? " · по типам" : " · по именам"),
    };

    /// <summary>Gets a value indicating whether the document describes something to show by itself.</summary>
    public bool IsVisual => _classification?.Kind
        is XamlDocumentKind.Window or XamlDocumentKind.UserControl or XamlDocumentKind.Control;

    /// <summary>Gets a value indicating whether the document is a look: styles, a theme, a dictionary.</summary>
    public bool IsLook => _classification?.Kind
        is XamlDocumentKind.TemplatedControl or XamlDocumentKind.Styles or XamlDocumentKind.ResourceDictionary;

    /// <summary>Gets a value indicating whether the root could not be resolved.</summary>
    public bool IsUnknown => _classification?.Kind is XamlDocumentKind.Unknown;

    /// <summary>Gets a value indicating whether the document is none of the above.</summary>
    public bool IsPlain => _classification?.Kind is XamlDocumentKind.Application or XamlDocumentKind.Other;

    /// <summary>Takes a new classification and tells the row about it.</summary>
    /// <param name="classification">What the classifier said.</param>
    internal void Update(XamlDocumentClassification classification)
    {
        _classification = classification;

        Raise(nameof(Kind));
        Raise(nameof(Detail));
        Raise(nameof(IsVisual));
        Raise(nameof(IsLook));
        Raise(nameof(IsUnknown));
        Raise(nameof(IsPlain));
    }

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
