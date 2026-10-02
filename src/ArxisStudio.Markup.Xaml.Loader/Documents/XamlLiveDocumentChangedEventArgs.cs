using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>Says what moved in a <see cref="XamlLiveDocument"/>.</summary>
/// <remarks>
/// Raised once per operation, after the operation is over, so a handler reading the document sees
/// the text, the session and the state together rather than one of them part-way.
/// </remarks>
public sealed class XamlLiveDocumentChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="changes">What moved.</param>
    public XamlLiveDocumentChangedEventArgs(XamlLiveDocumentChanges changes) => Changes = changes;

    /// <summary>Gets what moved.</summary>
    public XamlLiveDocumentChanges Changes { get; }
}
