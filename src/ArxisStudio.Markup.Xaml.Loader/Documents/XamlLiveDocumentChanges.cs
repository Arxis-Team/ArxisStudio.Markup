using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>What moved in a <see cref="XamlLiveDocument"/>, as one change reports it.</summary>
[Flags]
public enum XamlLiveDocumentChanges
{
    /// <summary>Nothing moved.</summary>
    None = 0,

    /// <summary>
    /// The text reads differently — an edit, an undo, a redo or text from outside — and so does the
    /// history.
    /// </summary>
    Text = 1,

    /// <summary>What is saved moved, or whether the document differs from it.</summary>
    Saved = 2,

    /// <summary>The state, the session or the diagnostics moved.</summary>
    State = 4,

    /// <summary>The document moved to another URI.</summary>
    Uri = 8,

    /// <summary>
    /// Objects that show the text were built again in place while the text stood still — the elements
    /// <see cref="XamlLiveDocument.RebuildAsync(Func{XamlDocument, System.Collections.Generic.IEnumerable{XamlElement}}, System.Threading.CancellationToken)"/>
    /// was asked for. Whatever held one of them holds an object that is no longer shown.
    /// </summary>
    Objects = 16,
}
