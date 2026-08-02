using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// A set of open XAML documents, edited through a history that can be undone.
/// </summary>
/// <remarks>
/// <para>
/// The seam between the two halves of this library. <see cref="MarkupWorkspace"/> knows how to
/// hold documents, version them and undo changes to them, and knows nothing about XAML;
/// <see cref="XamlDocumentEditor"/> knows how to express a structured edit as text changes, and
/// knows nothing about history. This puts the second into the first: one structured edit becomes
/// one entry in the undo stack, under a name a user would recognise.
/// </para>
/// <para>
/// A tool built on this does not need an undo stack of its own, and should not have one. Undo has
/// to be atomic across documents — moving a control and updating the resource dictionary it reads
/// from is one action, not two — and that is a property of the workspace rather than of any one
/// document.
/// </para>
/// <para>
/// The current parse of each open document is kept, so asking for one repeatedly — which a tool
/// drawing a tree does constantly — costs a dictionary lookup rather than a parse.
/// </para>
/// </remarks>
public sealed class XamlWorkspace : IDisposable
{
    private readonly ConcurrentDictionary<MarkupDocumentId, (DocumentVersion Version, XamlDocument Document)> _parsed
        = new();

    /// <summary>Creates a XAML workspace over a markup workspace.</summary>
    /// <param name="workspace">The workspace that holds the documents and their history.</param>
    /// <exception cref="ArgumentNullException"><paramref name="workspace"/> is <see langword="null"/>.</exception>
    public XamlWorkspace(MarkupWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        Workspace = workspace;
        Workspace.DocumentChanged += OnDocumentChanged;
    }

    /// <summary>Raised after a document is opened, changed, undone, redone or closed.</summary>
    public event EventHandler<XamlDocumentChangedEventArgs>? DocumentChanged;

    /// <summary>Gets the workspace the documents and their history live in.</summary>
    public MarkupWorkspace Workspace { get; }

    /// <summary>Gets a value indicating whether there is anything to undo.</summary>
    public bool CanUndo => Workspace.CanUndo;

    /// <summary>Gets a value indicating whether there is anything to redo.</summary>
    public bool CanRedo => Workspace.CanRedo;

    /// <summary>Gets the name of what undoing would undo.</summary>
    public string? UndoDescription => Workspace.UndoDescription;

    /// <summary>Gets the name of what redoing would redo.</summary>
    public string? RedoDescription => Workspace.RedoDescription;

    /// <summary>Opens a document through the workspace's source provider and parses it.</summary>
    /// <param name="uri">The document to open.</param>
    /// <param name="cancellationToken">A token to observe while opening.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public async ValueTask<XamlDocument> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        MarkupDocument document = await Workspace.OpenDocumentAsync(uri, cancellationToken).ConfigureAwait(false);

        return Parse(document);
    }

    /// <summary>Gets an open document as it now reads.</summary>
    /// <param name="id">The document to get.</param>
    /// <returns>The parsed document.</returns>
    public XamlDocument GetDocument(MarkupDocumentId id) => Parse(Workspace.GetDocument(id));

    /// <summary>
    /// Applies an editor's edits as one undoable action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The description is what the user will be offered to undo, so it should name the action
    /// rather than the mechanism: "Move Save into Footer", not "Apply 2 text changes".
    /// </para>
    /// <para>
    /// The editor must have been opened on the document as the workspace currently holds it. Its
    /// changes are spans into that exact text, and applying them to anything else would cut the
    /// document in the wrong places — so a stale editor is refused rather than approximated.
    /// </para>
    /// </remarks>
    /// <param name="editor">The editor holding the edits.</param>
    /// <param name="description">What to call this action in the undo history.</param>
    /// <returns>The document as it now reads.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="editor"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The editor's document is not open in this workspace, or the workspace has moved past the
    /// version it was opened on.
    /// </exception>
    public XamlDocument Apply(XamlDocumentEditor editor, string description)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return Apply(description, editor)[0];
    }

    /// <summary>
    /// Applies edits to several documents as one undoable action.
    /// </summary>
    /// <remarks>
    /// This is why undo belongs to the workspace rather than to a document. Moving a control and
    /// changing the resource dictionary it reads from is one thing the user did, and undoing half
    /// of it would leave the two disagreeing. Either every editor lands or none does.
    /// </remarks>
    /// <param name="description">What to call this action in the undo history.</param>
    /// <param name="editors">The editors, at most one per document.</param>
    /// <returns>The documents as they now read, in the order the editors were given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="editors"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// An editor's document is not open in this workspace, the workspace has moved past the
    /// version it was opened on, or two editors were given for the same document.
    /// </exception>
    public ImmutableArray<XamlDocument> Apply(string description, params XamlDocumentEditor[] editors)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(editors);

        var targets = new List<(MarkupDocument Document, ImmutableArray<TextChange> Changes)>(editors.Length);

        // Everything is located and every change computed before the transaction opens, so an
        // editor that cannot be applied refuses the action rather than half-making it.
        foreach (XamlDocumentEditor editor in editors)
        {
            ArgumentNullException.ThrowIfNull(editor);

            MarkupDocument document = Locate(editor.Document);

            if (targets.Any(target => target.Document.Id.Equals(document.Id)))
            {
                throw new InvalidOperationException(
                    $"Two editors were given for '{XamlUri.ToDisplayString(document.Uri)}'. Each was " +
                    "computed against the same text, so applying both would apply neither correctly. " +
                    "Record every edit to one document in one editor.");
            }

            targets.Add((document, editor.GetTextChanges()));
        }

        if (targets.All(static target => target.Changes.IsEmpty))
        {
            return [.. targets.Select(target => Parse(target.Document))];
        }

        // A transaction even for one document: it is what carries the description into the
        // history, and what makes an edit that spans two documents one thing to undo.
        using MarkupTransaction transaction = Workspace.BeginTransaction(description);

        foreach ((MarkupDocument document, ImmutableArray<TextChange> changes) in targets)
        {
            if (!changes.IsEmpty)
            {
                transaction.ApplyChanges(document.Id, changes);
            }
        }

        transaction.Commit();

        return [.. targets.Select(target => Parse(Workspace.GetDocument(target.Document.Id)))];
    }

    /// <summary>
    /// Stops listening to the workspace underneath and lets go of what has been parsed.
    /// </summary>
    /// <remarks>
    /// The subscription is what keeps this alive: a markup workspace outlives the views over it,
    /// and one that was closed without this would go on parsing every edit and raising events at
    /// whatever was still listening to it.
    /// </remarks>
    public void Dispose()
    {
        Workspace.DocumentChanged -= OnDocumentChanged;
        _parsed.Clear();
    }

    /// <summary>Undoes the last action.</summary>
    /// <returns><see langword="true"/> if there was one.</returns>
    public bool Undo() => Workspace.Undo();

    /// <summary>Redoes the last undone action.</summary>
    /// <returns><see langword="true"/> if there was one.</returns>
    public bool Redo() => Workspace.Redo();

    /// <summary>
    /// Finds the workspace document an editor was opened on, and refuses a stale one.
    /// </summary>
    private MarkupDocument Locate(XamlDocument document)
    {
        if (document.Uri is not { } uri || !Workspace.TryGetDocumentByUri(uri, out MarkupDocument? current))
        {
            throw new InvalidOperationException(
                "The editor's document is not open in this workspace. Open it with OpenAsync, or " +
                "apply the edits with XamlDocumentEditor.Apply and no history.");
        }

        if (current!.Text.ToString() != document.SourceText.ToString())
        {
            throw new InvalidOperationException(
                $"'{XamlUri.ToDisplayString(current.Uri)}' has changed since the editor was opened on " +
                "it. The edits are spans into the text it had then, so applying them now would cut " +
                "the document in the wrong places. Re-read the document and record the edits again.");
        }

        return current;
    }

    /// <summary>Parses a workspace document, or returns the parse already made of this version.</summary>
    /// <remarks>
    /// One entry per open document rather than one per version. A tool asks for the current
    /// document constantly — on every repaint of a tree — and holding every version it has ever
    /// had would grow for as long as the session lasts. Undoing costs one parse, and then the
    /// version it landed on is the cached one again.
    /// </remarks>
    private XamlDocument Parse(MarkupDocument document) =>
        _parsed.AddOrUpdate(
            document.Id,
            static (_, source) => (source.Version, XamlDocument.Parse(
                source.Text, new XamlParseOptions { DocumentUri = source.Uri })),
            static (_, existing, source) => existing.Version.Equals(source.Version)
                ? existing
                : (source.Version, XamlDocument.Parse(
                    source.Text, new XamlParseOptions { DocumentUri = source.Uri })),
            document)
            .Document;

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        // A closed document has no current version to hold a parse of, and keeping the last one
        // would make "one entry per open document" quietly untrue.
        if (e.Kind == DocumentChangeKind.Closed)
        {
            _parsed.TryRemove(e.DocumentId, out _);
        }

        if (DocumentChanged is not { } handler)
        {
            return;
        }

        handler(
            this,
            new XamlDocumentChangedEventArgs(
                e.Kind,
                e.DocumentId,
                e.NewDocument is null ? null : Parse(e.NewDocument)));
    }
}
