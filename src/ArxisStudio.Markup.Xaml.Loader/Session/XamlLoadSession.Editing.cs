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

        return XamlMemberResolver.Instance.Resolve(target.GetType(), name);
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

        XamlMemberDescriptor member = XamlMemberResolver.Instance.Resolve(target.GetType(), property);

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
            // only way to leave the two agreeing.
            target.SetValue(property, previous);

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

        XamlMemberDescriptor member = XamlMemberResolver.Instance.Resolve(target.GetType(), property);

        if (Reject(member, property, out XamlEditResult? rejected))
        {
            return rejected;
        }

        var diagnostics = new List<MarkupDiagnostic>();

        WarnIfExpressionWouldBeLost(target, property, diagnostics);

        if (value is XamlLiteralValue literal)
        {
            return SetValue(target, property, Convert(property, literal.Text, diagnostics));
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
        Objects = XamlObjectMap.Build(Document, RootObject, Projection);
    }

    /// <summary>Reads what the document says a property is.</summary>
    private XamlValue SourceValueOf(object target, AvaloniaProperty property)
    {
        XamlElement? element = Objects.GetElement(target);

        return element?.GetAttribute(property.Name)?.GetValue() ?? XamlValue.Unset;
    }

    /// <summary>Converts attribute text to the type a property holds.</summary>
    private static object? Convert(AvaloniaProperty property, string text, List<MarkupDiagnostic> diagnostics) =>
        XamlValueConversion.Convert(property.PropertyType, text, diagnostics);

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
