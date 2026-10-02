using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that says what the bindings of an element read from.
/// </summary>
public sealed partial class XamlLoadSession
{
    /// <summary>
    /// Works out what the bindings written on an element read from: the data type in scope and the
    /// design data its object shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The data type is read from the document — the nearest <c>x:DataType</c> on the element or above
    /// it, written as a type name or as <c>{x:Type}</c> — and resolved through the environment, as a
    /// load resolves it. The design data is read from the element's object, on the thread that owns it.
    /// </para>
    /// <para>
    /// Nothing is mutated, so this does not wait for an update in flight; asked during one, it answers
    /// for the document and the objects as they are at that moment.
    /// </para>
    /// </remarks>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>What the element's bindings read from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="element"/> is not an element of <see cref="Document"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlDataContextInfo> GetDataContextAsync(
        XamlElement element,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        XamlDocument document = Document;

        if (!ReferenceEquals(element.Document, document))
        {
            throw new ArgumentException(
                $"{element} is not an element of the session's document. Find it again in Document, by " +
                "path or by name.",
                nameof(element));
        }

        XamlElement? declaring = element.AncestorsAndSelf()
            .OfType<XamlElement>()
            .FirstOrDefault(static scope => scope.GetDirectiveAttribute(XamlDirectives.DataType) is not null);

        string? written = declaring?.GetDirective(XamlDirectives.DataType);

        Type? dataType = written is null
            ? null
            : await DataTypeAsync(written, declaring!, cancellationToken).ConfigureAwait(false);

        // The map and the objects both belong to the owning thread: an update in flight there is
        // rebuilding the one and writing the other.
        Type? designType = await _dispatcher
            .InvokeAsync(() => (GetObject(element) as StyledElement)?.DataContext?.GetType(), cancellationToken)
            .ConfigureAwait(false);

        return new XamlDataContextInfo
        {
            DataTypeElement = declaring,
            WrittenDataType = written,
            DataType = dataType,
            DesignDataContextType = designType,
            CompilesBindings = CompilesBindingsAt(element),
        };
    }

    /// <summary>
    /// Reports whether a binding written on an element is compiled: the nearest
    /// <c>x:CompileBindings</c> that reads as a truth value, or the session's default.
    /// </summary>
    private bool CompilesBindingsAt(XamlElement element)
    {
        foreach (XamlElement scope in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            if (scope.GetDirective(XamlDirectives.CompileBindings) is { } written
                && bool.TryParse(written, out bool compiled))
            {
                return compiled;
            }
        }

        return Options.UseCompiledBindingsByDefault;
    }

    /// <summary>
    /// Resolves an <c>x:DataType</c> as it is written — <c>vm:Main</c> or <c>{x:Type vm:Main}</c> —
    /// against the element that writes it.
    /// </summary>
    private async ValueTask<Type?> DataTypeAsync(string written, XamlElement scope, CancellationToken cancellationToken)
    {
        string name = written.Trim();

        if (XamlValue.Parse(name) is XamlMarkupExtensionValue extension)
        {
            if (!string.Equals(
                    scope.NamespaceContext.LookupNamespace(extension.TypeName.Prefix),
                    XamlNamespaces.Xaml,
                    StringComparison.Ordinal)
                || extension.TypeName.LocalName is not ("Type" or "TypeExtension")
                || (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("TypeName"))?.Value
                    is not XamlLiteralValue { Text: { } argument })
            {
                return null;
            }

            name = argument.Trim();
        }

        // A nested type or a namespace-qualified name is not something a document names by prefix.
        if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal) || name.Contains('+', StringComparison.Ordinal))
        {
            return null;
        }

        XamlQualifiedName qualified = XamlQualifiedName.Parse(name);

        if (scope.NamespaceContext.LookupNamespace(qualified.Prefix) is not { } namespaceUri)
        {
            return null;
        }

        XamlTypeResolution resolved = await Environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, qualified.LocalName), scope.NamespaceContext, cancellationToken)
            .ConfigureAwait(false);

        return resolved.Type;
    }
}
