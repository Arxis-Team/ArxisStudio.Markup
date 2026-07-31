namespace ArxisStudio.Markup;

/// <summary>What happened to a document in a <see cref="MarkupWorkspace"/>.</summary>
public enum DocumentChangeKind
{
    /// <summary>The document was added to the workspace.</summary>
    Opened,

    /// <summary>The document's text was replaced, producing a new version.</summary>
    Changed,

    /// <summary>The document was removed from the workspace.</summary>
    Closed,
}
