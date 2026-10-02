using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What an operation on a <see cref="XamlLiveDocument"/> did to its text and to what shows it.
/// </summary>
/// <remarks>
/// Two questions with separate answers. Whether the text moved is settled before anything is shown:
/// an edit is recorded in the history whatever the objects make of it, because the document is the
/// truth and a change the objects could not follow is still a change to the file. What shows it
/// afterwards is <see cref="State"/>, and <see cref="Update"/> and <see cref="Load"/> are the
/// session's own reports of how it got there.
/// </remarks>
public sealed class XamlLiveEditResult
{
    /// <summary>Gets a value indicating whether the text moved — and with it the history.</summary>
    public required bool TextChanged { get; init; }

    /// <summary>
    /// Gets a value indicating whether a different session, or none, now stands where the session
    /// was. <see cref="XamlLiveDocument.SessionReplaced"/> was raised for it.
    /// </summary>
    public required bool SessionReplaced { get; init; }

    /// <summary>Gets what the objects show of the text now.</summary>
    public required XamlLiveDocumentState State { get; init; }

    /// <summary>
    /// Gets what the session's update made of the text, when the session in place was asked to
    /// follow it.
    /// </summary>
    public XamlUpdateResult? Update { get; init; }

    /// <summary>
    /// Gets what loading the text did, when a new session was built from it — because the session
    /// could not follow, because there was none, or because one was asked for.
    /// </summary>
    public XamlLoadResult? Load { get; init; }

    /// <summary>Gets what the document's <see cref="XamlLiveDocument.Diagnostics"/> say now.</summary>
    public required ImmutableArray<MarkupDiagnostic> Diagnostics { get; init; }
}
