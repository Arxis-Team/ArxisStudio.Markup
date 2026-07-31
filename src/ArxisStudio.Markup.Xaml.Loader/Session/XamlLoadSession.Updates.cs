using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that brings its objects in line with a document that has changed
/// underneath them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here compiles anything. A document is text, the objects are built from text, and an
/// update is a comparison of two syntax trees followed by the smallest change that is certainly
/// enough — which is what makes editing XAML and seeing the result a thing that can happen
/// without a build.
/// </para>
/// <para>
/// An update that cannot be applied leaves the objects exactly as they were. The document that
/// was offered is kept as <see cref="PendingDocument"/> rather than dropped, because the usual
/// reason an update fails is that the author is halfway through typing it, and the next keystroke
/// is the correction.
/// </para>
/// </remarks>
public sealed partial class XamlLoadSession
{
    /// <summary>
    /// Gets the most recent document that was offered and not applied, if there is one.
    /// </summary>
    /// <remarks>
    /// A refused update is not a discarded one. This is what was refused, so a caller can show
    /// it, diff it, or hand back a corrected version of it; it is cleared as soon as an update
    /// lands.
    /// </remarks>
    public XamlDocument? PendingDocument { get; private set; }

    /// <summary>
    /// Brings the objects in line with a changed version of the session's document.
    /// </summary>
    /// <param name="updated">The document as it now reads.</param>
    /// <param name="cancellationToken">A token to observe while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updated"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public async ValueTask<XamlUpdateResult> ApplyDocumentUpdateAsync(
        XamlDocument updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<MarkupDiagnostic>();

        // A document that did not parse describes nothing to update towards, and the errors
        // saying why are more use than anything an attempt would produce.
        if (!updated.IsWellFormed)
        {
            diagnostics.AddRange(updated.GetDiagnostics().Where(static diagnostic => diagnostic.IsError));

            return Refuse(
                updated,
                XamlUpdateStrategy.None,
                [],
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRejected,
                "The document offered does not parse, so the objects were left as they were.");
        }

        ImmutableArray<XamlDocumentChange> changes = XamlDocumentDiff.Compare(Document, updated);
        XamlUpdateStrategy strategy = XamlDocumentDiff.Largest(changes);

        if (strategy == XamlUpdateStrategy.RecreateSession)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateRequiresNewSession,
                "The root element or x:Class changed. The objects were left as they were; create a " +
                "new session from the new document.");
        }

        if (strategy > XamlUpdateStrategy.UpdateDesignValue)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                $"This update needs {strategy}, which this session cannot yet apply in place. " +
                "The objects were left as they were.");
        }

        return await ApplyAsync(updated, strategy, changes, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the objects in line with a resource file that has changed outside the document.
    /// </summary>
    /// <remarks>
    /// An included dictionary or style file is part of the text the objects were built from, so a
    /// change to one is a change to the load even though the document itself reads the same. The
    /// document is reprojected — which is what re-reads the file through the environment's
    /// resolvers — and the difference that makes decides what has to be rebuilt.
    /// </remarks>
    /// <param name="resourceUri">The resource that changed.</param>
    /// <param name="cancellationToken">A token to observe while updating.</param>
    /// <returns>What the update did, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resourceUri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public async ValueTask<XamlUpdateResult> ApplySourceUpdateAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var diagnostics = new List<MarkupDiagnostic>();

        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(Document, Environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Nothing the document reaches changed, whatever the caller was told about the file.
        if (string.Equals(projection.Text.ToString(), Projection.Text.ToString(), StringComparison.Ordinal))
        {
            return new XamlUpdateResult
            {
                Applied = true,
                Strategy = XamlUpdateStrategy.None,
                Changes = [],
                Diagnostics = [.. diagnostics],
            };
        }

        // The content the document pulls in is not the document, so there is no element of it to
        // set a property on. Everything it produced has to be built again from the new text.
        return Refuse(
            Document,
            XamlUpdateStrategy.ReplaceResource,
            [new XamlDocumentChange(XamlUpdateStrategy.ReplaceResource, null, null, null)],
            diagnostics,
            XamlLoaderDiagnosticCodes.UpdateNotApplied,
            $"'{XamlUri.ToDisplayString(resourceUri)}' changed what the document includes, which needs " +
            "ReplaceResource. This session cannot yet apply that in place, and the objects were left " +
            "as they were.");
    }

    /// <summary>Applies an update that can be made on the objects that already exist.</summary>
    private async ValueTask<XamlUpdateResult> ApplyAsync(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        // Reprojecting before anything is touched means a failure to resolve an include is a
        // refused update rather than a half-updated tree.
        TextProjection projection = await XamlDocumentProjector
            .ProjectAsync(updated, Environment, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        bool applied = await _dispatcher
            .InvokeAsync(() => Write(changes, diagnostics), cancellationToken)
            .ConfigureAwait(false);

        if (!applied)
        {
            return Refuse(
                updated,
                strategy,
                changes,
                diagnostics,
                XamlLoaderDiagnosticCodes.UpdateNotApplied,
                "The update could not be applied, and the objects were left as they were.");
        }

        Document = updated;
        Projection = projection;
        Objects = XamlObjectMap.Build(updated, RootObject, projection);
        PendingDocument = null;

        // Design values are re-applied from the document rather than patched one at a time.
        // Nothing re-evaluates on an update, so the document is the only place that says what
        // they are now, and applying all of them is the same walk a design-mode load does.
        if (Options.Mode == XamlLoadMode.Design)
        {
            await _dispatcher
                .InvokeAsync<object?>(
                    () =>
                    {
                        XamlDesignValues.Apply(updated, Objects, RootObject, diagnostics);

                        return null;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new XamlUpdateResult
        {
            Applied = true,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }

    /// <summary>
    /// Writes every change onto the objects, or writes none of them.
    /// </summary>
    /// <remarks>
    /// Each change is checked before any is made. A run that stopped halfway would leave the
    /// objects agreeing with neither document, and nothing would say where the boundary was.
    /// </remarks>
    private bool Write(ImmutableArray<XamlDocumentChange> changes, List<MarkupDiagnostic> diagnostics)
    {
        var writes = new List<(object Target, XamlMemberDescriptor Member, object? Value)>();

        foreach (XamlDocumentChange change in changes)
        {
            if (change.Strategy == XamlUpdateStrategy.UpdateDesignValue)
            {
                // Design values are reapplied wholesale once the document has advanced, because
                // that is the same walk a design-mode load does and there is only one of it.
                continue;
            }

            if (change.OldElement is not { } element
                || change.NewElement is not { } updatedElement
                || change.MemberName is not { } name)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"{change} does not say which member of which element changed.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri));

                return false;
            }

            if (Objects.GetObject(element) is not { } target)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UpdateNotApplied,
                    $"<{element.Name}> produced no object, so {name} has nothing to be set on.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return false;
            }

            XamlMemberDescriptor member = XamlMemberResolver.Instance.Resolve(target.GetType(), name);

            if (!member.IsResolved || member.IsReadOnly || !member.CanWrite)
            {
                diagnostics.Add(MarkupDiagnostic.Synchronization(
                    XamlLoaderDiagnosticCodes.UnresolvedMember,
                    $"{name} is not a writable member of {target.GetType().Name}.",
                    MarkupDiagnosticSeverity.Error,
                    Document.Uri,
                    element.NameSpan));

                return false;
            }

            string text = updatedElement.GetAttribute(XamlQualifiedName.Parse(name))?.GetValueText() ?? string.Empty;

            writes.Add((target, member, XamlValueConversion.Convert(member.ValueType, text, diagnostics)));
        }

        foreach ((object target, XamlMemberDescriptor member, object? value) in writes)
        {
            XamlDesignValues.Write(target, member, value);
        }

        return true;
    }

    /// <summary>Records an update that was not applied, keeping the document it was offered.</summary>
    private XamlUpdateResult Refuse(
        XamlDocument updated,
        XamlUpdateStrategy strategy,
        ImmutableArray<XamlDocumentChange> changes,
        List<MarkupDiagnostic> diagnostics,
        string code,
        string message)
    {
        PendingDocument = updated;

        diagnostics.Add(MarkupDiagnostic.Synchronization(
            code,
            message,
            code == XamlLoaderDiagnosticCodes.UpdateRejected
                ? MarkupDiagnosticSeverity.Error
                : MarkupDiagnosticSeverity.Warning,
            updated.Uri));

        return new XamlUpdateResult
        {
            Applied = false,
            Strategy = strategy,
            Changes = changes,
            Diagnostics = [.. diagnostics],
        };
    }
}
