using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Diagnostics;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that reads and changes the objects it loaded.
/// </summary>
public sealed partial class XamlLoadSession
{
    /// <summary>Gets the element an object was declared by.</summary>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>
    /// The element, or <see langword="null"/> when a template, a style or run-time code created
    /// the object rather than this document.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public XamlElement? GetElement(object runtimeObject)
    {
        VerifyAccess();

        return Objects.GetElement(runtimeObject);
    }

    /// <summary>Gets the object an element produced.</summary>
    /// <param name="element">The element to look up.</param>
    /// <returns>The object, or <see langword="null"/> when the element produced none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public object? GetObject(XamlElement element)
    {
        VerifyAccess();

        return Objects.GetObject(element);
    }

    /// <summary>Gets what an object came from.</summary>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>Its origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public XamlObjectOrigin GetOrigin(object runtimeObject)
    {
        VerifyAccess();

        return Objects.GetOrigin(runtimeObject);
    }

    /// <summary>Gets the document an object was declared in.</summary>
    /// <remarks>
    /// Not always this document. An object created by a <c>ResourceInclude</c> or a
    /// <c>StyleInclude</c> is declared in the file the include names, and a caller offering to
    /// navigate to a declaration needs to open that file rather than this one.
    /// </remarks>
    /// <param name="runtimeObject">The object to look up.</param>
    /// <returns>The URI of the file its markup is in, or <see langword="null"/> when it has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeObject"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public Uri? GetSourceUri(object runtimeObject)
    {
        VerifyAccess();

        return Objects.GetSourceUri(runtimeObject);
    }

    /// <summary>Works out what a member name means on an object.</summary>
    /// <param name="target">The object the member is written on.</param>
    /// <param name="name">The member name, which may have the <c>Owner.Member</c> shape.</param>
    /// <returns>What the member is, or an unresolved descriptor when nothing matched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    public XamlMemberDescriptor GetMember(object target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(name);
        VerifyAccess();

        return Environment.MemberResolver.Resolve(target.GetType(), name);
    }

    /// <summary>
    /// Reports what a property is currently set to, where that came from, and what the document
    /// says about it.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="property">The property to inspect.</param>
    /// <returns>The property's state on both sides.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public XamlValueInfo GetValueInfo(AvaloniaObject target, AvaloniaProperty property)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        VerifyAccess();

        AvaloniaPropertyValue diagnostic = target.GetDiagnostic(property);
        bool hasBinding = BindingOperations.GetBindingExpressionBase(target, property) is not null;

        return new XamlValueInfo(
            property,
            diagnostic.Value,
            hasBinding ? XamlValueSource.Binding : Translate(diagnostic.Priority),
            SourceValueOf(target, property),
            hasBinding);
    }

    /// <summary>
    /// Lists the members of an object that a document can set.
    /// </summary>
    /// <remarks>
    /// What a property panel is built from. Which of them to show is the caller's decision; what
    /// each one is — a styled property, an attached one, an event, what it holds and whether it
    /// can be written — is answered here.
    /// </remarks>
    /// <param name="target">The object to list.</param>
    /// <returns>The members, ordered by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public ImmutableArray<XamlMemberDescriptor> GetMembers(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Environment.MemberResolver.Enumerate(target.GetType());
    }

    /// <summary>
    /// Sets a property on the object and in the document, as one operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The member is validated, the value converted, the object updated and the document
    /// updated — in that order, and if writing the document fails the object is put back. The
    /// two never end up silently disagreeing.
    /// </para>
    /// <para>
    /// Replacing a value the document expresses as a binding or a resource reference is allowed,
    /// because a caller may mean exactly that, but it is reported: an edit that destroys an
    /// expression should never happen unnoticed.
    /// </para>
    /// <para>
    /// This writes the session's own document and creates no undo entry. A tool with a history
    /// records its edits on a <c>XamlDocumentEditor</c>, applies them through
    /// <c>XamlWorkspace</c> and brings the objects into line with
    /// <see cref="ApplyDocumentUpdateAsync" /> — see
    /// <c>docs/adr/0007-undo-belongs-to-the-workspace.md</c>. The two directions must not be
    /// mixed on one document: the session's document is not the workspace's, and this would
    /// advance one while the other stood still.
    /// </para>
    /// <para>
    /// This mutates the session, so it passes through the same gate the asynchronous updates use —
    /// but it cannot wait for it. Blocking here would block a thread that may be the very one an
    /// update is dispatching to, which is a deadlock rather than a delay. It therefore fails fast
    /// with <see cref="XamlLoaderDiagnosticCodes.SessionBusy"/> when an update is running, and the
    /// caller retries or awaits the update it already has.
    /// </para>
    /// </remarks>
    /// <param name="target">The object to change.</param>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>Whether the edit was applied, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public XamlEditResult SetValue(AvaloniaObject target, AvaloniaProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        VerifyAccess();

        if (UnusableForEdit() is { } unusable)
        {
            return unusable;
        }

        if (!_mutation.Wait(0))
        {
            return Busy();
        }

        try
        {
            return SetValueCore(target, property, value);
        }
        finally
        {
            _mutation.Release();
        }
    }

    /// <summary>Sets a property with the mutation gate already held.</summary>
    private XamlEditResult SetValueCore(AvaloniaObject target, AvaloniaProperty property, object? value)
    {
        XamlMemberDescriptor member = Environment.MemberResolver.Resolve(target.GetType(), property);

        if (Reject(member, property, out XamlEditResult? rejected))
        {
            return rejected;
        }

        var diagnostics = new List<MarkupDiagnostic>();

        WarnIfExpressionWouldBeLost(target, property, diagnostics);

        object? previous = target.GetValue(property);

        try
        {
            target.SetValue(property, value);
        }
        catch (Exception error)
        {
            return Failure(
                diagnostics,
                XamlLoaderDiagnosticCodes.IncompatibleValue,
                $"'{value}' could not be assigned to {property.OwnerType.Name}.{property.Name}: {error.Message}");
        }

        string text = Format(value);

        try
        {
            UpdateDocument(target, property.Name, new XamlLiteralValue(text), diagnostics);
        }
        catch (Exception)
        {
            // The object was changed and the document was not. Putting the object back is the
            // only way to leave the two agreeing — and where the property will not take its own
            // old value back, nothing here can make them agree, so the session says so rather
            // than letting the next edit be written onto a tree that describes nothing.
            try
            {
                target.SetValue(property, previous);
            }
            catch (Exception)
            {
                RequireRecreation();
            }

            throw;
        }

        return new XamlEditResult { Applied = true, Diagnostics = [.. diagnostics] };
    }

    /// <summary>
    /// Sets a property from a value written the way the document writes it.
    /// </summary>
    /// <remarks>
    /// This is how a binding is set as a binding. The document records the expression, and the
    /// object is left for the next load to populate rather than being given the expression's
    /// text as a literal — writing <c>"{Binding Name}"</c> into a string property would be a
    /// quietly wrong thing to do.
    /// </remarks>
    /// <param name="target">The object to change.</param>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value as the document should express it.</param>
    /// <returns>Whether the edit was applied, and everything noticed on the way.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the objects.</exception>
    public XamlEditResult SetXamlValue(AvaloniaObject target, AvaloniaProperty property, XamlValue value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        VerifyAccess();

        if (UnusableForEdit() is { } unusable)
        {
            return unusable;
        }

        if (!_mutation.Wait(0))
        {
            return Busy();
        }

        try
        {
            XamlMemberDescriptor member = Environment.MemberResolver.Resolve(target.GetType(), property);

            if (Reject(member, property, out XamlEditResult? rejected))
            {
                return rejected;
            }

            var diagnostics = new List<MarkupDiagnostic>();

            WarnIfExpressionWouldBeLost(target, property, diagnostics);

            if (value is XamlLiteralValue literal)
            {
                XamlValueConversionResult converted = member.ConvertFromText(literal.Text);

                if (!converted.Succeeded)
                {
                    // The same refusal an update would report, said before either side is touched.
                    return Failure(
                        diagnostics,
                        XamlLoaderDiagnosticCodes.IncompatibleValue,
                        $"{target.GetType().Name}.{property.Name} cannot be set: {converted.Error}");
                }

                // The core rather than the public method: the gate is this operation's already,
                // and taking it a second time would refuse the caller its own edit.
                return SetValueCore(target, property, converted.Value);
            }

            // An expression cannot be evaluated here — resolving it is what a load does. The
            // document is updated and the object is left alone rather than given something wrong.
            UpdateDocument(target, property.Name, value, diagnostics);

            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.ExpressionNotApplied,
                $"{property.Name} now reads '{value.ToXamlText()}' in the document. " +
                "The object keeps its current value until the document is loaded again.",
                MarkupDiagnosticSeverity.Info,
                Document.Uri));

            return new XamlEditResult { Applied = true, Diagnostics = [.. diagnostics] };
        }
        finally
        {
            _mutation.Release();
        }
    }

    /// <summary>
    /// Refuses an edit outright when an earlier update left the session describing nothing.
    /// </summary>
    /// <remarks>
    /// The same refusal the update path makes, in the shape an edit answers with. Named apart from
    /// it because the two live in different halves of this class and differ only by return type,
    /// which is exactly the pair a reader mistakes for one method.
    /// </remarks>
    private XamlEditResult? UnusableForEdit() =>
        State == XamlSessionState.Usable
            ? null
            : new XamlEditResult
            {
                Applied = false,
                Diagnostics =
                [
                    MarkupDiagnostic.Synchronization(
                        XamlLoaderDiagnosticCodes.SessionRequiresRecreation,
                        "An earlier update stopped after it had begun writing to the objects. This session " +
                        "accepts no further change; create a new session from the document you want.",
                        MarkupDiagnosticSeverity.Error,
                        Document.Uri),
                ],
            };

    /// <summary>Refuses an edit that arrived while an update owned the session.</summary>
    /// <remarks>
    /// A refusal rather than a wait, and rather than an exception: an edit racing an update is
    /// something a host can meet in ordinary use — a property field committed while a file watcher
    /// reloads the document — and the answer to it is to try again, not to unwind.
    /// </remarks>
    private XamlEditResult Busy() => new()
    {
        Applied = false,
        Diagnostics =
        [
            MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.SessionBusy,
                "An update is running on this session, and a synchronous edit does not wait for one. " +
                "Nothing was written; await the update and make the edit again.",
                MarkupDiagnosticSeverity.Warning,
                Document.Uri),
        ],
    };

    /// <summary>Rejects an edit the member itself rules out.</summary>
    private XamlEditResult? RejectedOrNull(XamlMemberDescriptor member, AvaloniaProperty property) =>
        member.Kind switch
        {
            XamlMemberKind.Unknown => Failure(
                [],
                XamlLoaderDiagnosticCodes.UnresolvedMember,
                $"{property.OwnerType.Name}.{property.Name} is not a member of {member.TargetType.Name}."),
            _ when member.IsReadOnly => Failure(
                [],
                XamlLoaderDiagnosticCodes.ReadOnlyMember,
                $"{property.OwnerType.Name}.{property.Name} is read-only and cannot be set."),
            _ => null,
        };

    private bool Reject(
        XamlMemberDescriptor member,
        AvaloniaProperty property,
        out XamlEditResult rejected)
    {
        XamlEditResult? result = RejectedOrNull(member, property);

        rejected = result ?? new XamlEditResult { Applied = false, Diagnostics = [] };

        return result is not null;
    }

    /// <summary>Reports an edit that is about to replace an expression with a literal.</summary>
    private void WarnIfExpressionWouldBeLost(
        AvaloniaObject target,
        AvaloniaProperty property,
        List<MarkupDiagnostic> diagnostics)
    {
        XamlValueInfo info = GetValueInfo(target, property);

        if (info.WouldDestroyExpression)
        {
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.ExpressionReplaced,
                $"{property.Name} was written as '{info.SourceValue.ToXamlText()}'. Setting it replaces that expression.",
                MarkupDiagnosticSeverity.Warning,
                Document.Uri));
        }
    }

    /// <summary>Writes the change into the document, advancing the session's document.</summary>
    private void UpdateDocument(
        object target,
        string memberName,
        XamlValue value,
        List<MarkupDiagnostic> diagnostics)
    {
        XamlElement? element = Objects.GetElement(target);

        if (element is null)
        {
            // Nothing in this document declares the object, so there is nothing to write. That
            // is the normal case for a template-generated child, and saying so beats writing
            // the edit into the nearest element that happens to be mapped.
            diagnostics.Add(MarkupDiagnostic.Synchronization(
                XamlLoaderDiagnosticCodes.NoSourceDeclaration,
                $"{target.GetType().Name} has no declaration in this document, so the change was applied " +
                "to the object only.",
                MarkupDiagnosticSeverity.Warning,
                Document.Uri));

            return;
        }

        Document = Document.SetAttribute(element, XamlQualifiedName.Parse(memberName), value);

        // Editing reparses, so every element the caller holds now describes text that has moved.
        // The projection is the one the objects were built from, because the positions being
        // read back are the ones Avalonia recorded against it.
        Objects = XamlObjectMap.Build(
            Document,
            RootObject,
            Projection,
            System.Collections.Immutable.ImmutableDictionary<Uri, TextProjection>.Empty,
            carried: null,
            Environment.MemberResolver);
    }

    /// <summary>Reads what the document says a property is.</summary>
    private XamlValue SourceValueOf(object target, AvaloniaProperty property)
    {
        XamlElement? element = Objects.GetElement(target);

        return element?.GetAttribute(property.Name)?.GetValue() ?? XamlValue.Unset;
    }

    /// <summary>Renders a value as the document would write it.</summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static XamlValueSource Translate(BindingPriority priority) => priority switch
    {
        BindingPriority.LocalValue => XamlValueSource.Local,
        BindingPriority.StyleTrigger => XamlValueSource.StyleTrigger,
        BindingPriority.Template => XamlValueSource.Template,
        BindingPriority.Style => XamlValueSource.Style,
        BindingPriority.Inherited => XamlValueSource.Inherited,
        BindingPriority.Animation => XamlValueSource.Animation,
        _ => XamlValueSource.Unset,
    };

    private XamlEditResult Failure(List<MarkupDiagnostic> diagnostics, string code, string message)
    {
        var all = new List<MarkupDiagnostic>(diagnostics)
        {
            MarkupDiagnostic.Synchronization(code, message, MarkupDiagnosticSeverity.Error, Document.Uri),
        };

        return new XamlEditResult { Applied = false, Diagnostics = [.. all] };
    }
}
