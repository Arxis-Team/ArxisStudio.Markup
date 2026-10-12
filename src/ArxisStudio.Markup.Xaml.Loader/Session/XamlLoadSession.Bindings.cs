using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data;
using Avalonia.Diagnostics;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of a session that checks a binding written on an element against what the document declares.
/// </summary>
public sealed partial class XamlLoadSession
{
    /// <summary>The member a data context is written as; a binding on it reads the data its parent has.</summary>
    private const string DataContextMember = "DataContext";

    /// <summary>
    /// Checks a binding written on an element: what it reads from, whether its path names something there, and
    /// whether a build that compiles it would take it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source is read from the binding as written and found in the document, not in the tree the objects
    /// stand in: <c>#name</c> and <c>ElementName</c> name an element of the document's name scope — a template's
    /// own inside a template — <c>$parent[T]</c> and <c>FindAncestor</c> an element above in the document, and
    /// where the document has no such ancestor, the type <c>T</c> itself, which is what a build reads. A host
    /// shows the root inside its own controls, and an ancestor or a data context found there would be the
    /// host's.
    /// </para>
    /// <para>
    /// The data context is read as a build reads it: a compiled binding against the nearest <c>x:DataType</c>,
    /// which a data template does not inherit, and without one in scope the build refuses it. A binding a build
    /// does not compile is read as it reads when the form runs — against the data the document gives the objects,
    /// written on an element, or set by the class or the design data on the root — and against the data type in
    /// scope only where there is no such data; data the root inherits from the host is not the document's. A
    /// binding on <c>DataContext</c> itself reads its parent's.
    /// </para>
    /// <para>
    /// The path is read by <see cref="XamlMemberResolver.ResolveBindingPath"/>, and what it does not follow —
    /// a <c>Source</c>, a templated parent, a cast — is <see cref="XamlBindingPathStatus.NotUnderstood"/>,
    /// never broken.
    /// </para>
    /// </remarks>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <param name="member">The member the binding is written on, as the document names it.</param>
    /// <param name="binding">The binding as written: <c>{Binding}</c>, <c>{CompiledBinding}</c>, <c>{ReflectionBinding}</c>.</param>
    /// <param name="compilesByDefault">
    /// Whether a <c>{Binding}</c> with no <c>x:CompileBindings</c> in scope compiles in the build being asked
    /// about — a project's own default, which a session that loads forgivingly does not share.
    /// </param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>What the binding reads and whether its path is there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="member"/> is empty, or <paramref name="element"/> is not an element of <see cref="Document"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<XamlBindingCheck> CheckBindingAsync(
        XamlElement element,
        string member,
        XamlMarkupExtensionValue binding,
        bool compilesByDefault,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(member);
        ArgumentNullException.ThrowIfNull(binding);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!ReferenceEquals(element.Document, Document))
        {
            throw new ArgumentException(
                $"{element} is not an element of the session's document. Find it again in Document, by path or by name.",
                nameof(element));
        }

        bool compiles = Compiles(binding, element, compilesByDefault);
        WrittenBinding written = WrittenBinding.Read(binding);

        XamlBindingCheck Check(XamlBindingPathStatus status, Type? source, string? step, string message) => new()
        {
            Source = written.Source,
            Status = status,
            Compiles = compiles,
            Path = written.Path,
            SourceType = source,
            Step = step,
            Message = message,
        };

        (Type? source, XamlBindingCheck? stopped) = written.Source switch
        {
            XamlBindingSource.DataContext => await DataSourceAsync(element, member, compiles, Check, cancellationToken).ConfigureAwait(false),
            XamlBindingSource.Element => await NamedSourceAsync(element, written.Name!, Check, cancellationToken).ConfigureAwait(false),
            XamlBindingSource.Self => (await ElementTypeAsync(element, cancellationToken).ConfigureAwait(false), null),
            XamlBindingSource.Ancestor => await AncestorSourceAsync(element, written, Check, cancellationToken).ConfigureAwait(false),
            _ => (null, Check(XamlBindingPathStatus.NotUnderstood, null, null, "The binding reads a source this reading does not follow.")),
        };

        if (stopped is not null)
        {
            return stopped;
        }

        if (source is null)
        {
            return Check(XamlBindingPathStatus.NotUnderstood, null, null, "What the binding reads is known only when the form runs.");
        }

        XamlBindingPathResult path = Environment.MemberResolver.ResolveBindingPath(source, written.Path);

        return new XamlBindingCheck
        {
            Source = written.Source,
            Status = path.Status,
            Compiles = compiles,
            Path = written.Path,
            SourceType = source,
            ResultType = path.ResultType,
            Step = path.Step,
            Message = path.Message,
        };
    }

    /// <summary>The data a binding reads: the data type in scope, or the data the document gives the objects.</summary>
    private async ValueTask<(Type? Source, XamlBindingCheck? Stopped)> DataSourceAsync(
        XamlElement element,
        string member,
        bool compiles,
        Func<XamlBindingPathStatus, Type?, string?, string, XamlBindingCheck> check,
        CancellationToken cancellationToken)
    {
        // A data context binding reads what the parent has: the element's own x:DataType is about the data it
        // gives its content, not about where that data comes from.
        XamlElement? scope = string.Equals(member, DataContextMember, StringComparison.Ordinal) ? Owner(element) : element;

        if (scope is null)
        {
            return (null, check(XamlBindingPathStatus.NotUnderstood, null, null, "The root's data context binding reads what its host gives it."));
        }

        (XamlElement? declaring, bool inOtherTemplate) = DataTypeScope(scope);

        if (inOtherTemplate)
        {
            return (null, check(XamlBindingPathStatus.NotUnderstood, null, null, "Inside a template the data is the templated control's."));
        }

        string? writtenType = declaring?.GetDirective(XamlDirectives.DataType);
        Type? dataType = writtenType is null
            ? null
            : await DataTypeAsync(writtenType, declaring!, cancellationToken).ConfigureAwait(false);

        if (compiles)
        {
            if (declaring is null)
            {
                return (null, check(
                    XamlBindingPathStatus.Broken,
                    null,
                    null,
                    "A compiled binding reads its path against x:DataType, and none is in scope."));
            }

            return dataType is null
                ? (null, check(XamlBindingPathStatus.NotUnderstood, null, null, $"x:DataType '{writtenType!.Trim()}' names nothing the environment resolves."))
                : (dataType, null);
        }

        // A binding the build does not compile reads, when the form runs, the data its element has: x:DataType is what
        // a compiler checks against, and it is a guess here only where the document gives the objects no data.
        return (await DocumentDataTypeAsync(scope, cancellationToken).ConfigureAwait(false) ?? dataType, null);
    }

    /// <summary>The element a binding names: one of the document's name scope, or the binding reads nothing.</summary>
    private async ValueTask<(Type? Source, XamlBindingCheck? Stopped)> NamedSourceAsync(
        XamlElement element,
        string name,
        Func<XamlBindingPathStatus, Type?, string?, string, XamlBindingCheck> check,
        CancellationToken cancellationToken)
    {
        XamlElement? template = Template(element);
        XamlElement? named = Document.DescendantElements()
            .FirstOrDefault(candidate => string.Equals(candidate.Identity, name, StringComparison.Ordinal) &&
                                         ReferenceEquals(Template(candidate), template));

        if (named is null)
        {
            return (null, check(XamlBindingPathStatus.Broken, null, name, $"No element is named '{name}' where the binding can see it."));
        }

        return (await ElementTypeAsync(named, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// The ancestor a binding names: the one of that type and level above the element in the document, and where
    /// the document has none, the type itself — the ancestor stands outside the document, and a build reads the type.
    /// </summary>
    private async ValueTask<(Type? Source, XamlBindingCheck? Stopped)> AncestorSourceAsync(
        XamlElement element,
        WrittenBinding written,
        Func<XamlBindingPathStatus, Type?, string?, string, XamlBindingCheck> check,
        CancellationToken cancellationToken)
    {
        Type? wanted = null;

        if (written.Name is { } typeName)
        {
            wanted = await DataTypeAsync(typeName, element, cancellationToken).ConfigureAwait(false);

            if (wanted is null)
            {
                return (null, check(XamlBindingPathStatus.Broken, null, typeName, $"'{typeName}' names no type the environment resolves."));
            }
        }

        int found = 0;

        for (XamlElement? ancestor = Owner(element); ancestor is not null; ancestor = Owner(ancestor))
        {
            if (await ElementTypeAsync(ancestor, cancellationToken).ConfigureAwait(false) is not { } type ||
                wanted?.IsAssignableFrom(type) == false)
            {
                continue;
            }

            if (++found == written.Level)
            {
                return (type, null);
            }
        }

        return (wanted, null);
    }

    /// <summary>
    /// The data the document gives an element's object: the nearest data context, on the element or above it, that
    /// is not inherited — what an element writes, or what the class or the design data sets on the root. What the
    /// root inherits comes from its host.
    /// </summary>
    private ValueTask<Type?> DocumentDataTypeAsync(XamlElement scope, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(
            () =>
            {
                for (XamlElement? at = scope; at is not null; at = Owner(at))
                {
                    if (GetObject(at) is not StyledElement owner ||
                        owner.GetDiagnostic(StyledElement.DataContextProperty).Priority is BindingPriority.Inherited or BindingPriority.Unset)
                    {
                        continue;
                    }

                    return owner.DataContext?.GetType();
                }

                return null;
            },
            cancellationToken);

    /// <summary>The type of what an element builds: its object's, or, where it built none, the type its name resolves to.</summary>
    private async ValueTask<Type?> ElementTypeAsync(XamlElement element, CancellationToken cancellationToken)
    {
        if (await _dispatcher.InvokeAsync(() => GetObject(element)?.GetType(), cancellationToken).ConfigureAwait(false) is { } built)
        {
            return built;
        }

        if (element.NamespaceUri is not { } namespaceUri)
        {
            return null;
        }

        XamlTypeResolution resolved = await Environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, element.Name.LocalName), element.NamespaceContext, cancellationToken)
            .ConfigureAwait(false);

        return resolved.Type;
    }

    /// <summary>
    /// The element whose <c>x:DataType</c> is in scope at an element — none past a data template, whose content reads
    /// the data it is given — and whether the walk passed another template first, whose content reads the templated
    /// control's data.
    /// </summary>
    private static (XamlElement? Declaring, bool InOtherTemplate) DataTypeScope(XamlElement element)
    {
        foreach (XamlElement at in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            if (at.GetDirectiveAttribute(XamlDirectives.DataType) is not null)
            {
                return (at, false);
            }

            if (IsTemplate(at))
            {
                return (null, !at.Name.LocalName.EndsWith("DataTemplate", StringComparison.Ordinal));
            }
        }

        return (null, false);
    }

    /// <summary>Whether a <c>{Binding}</c> compiles in the build: its own kind, the nearest <c>x:CompileBindings</c>, or the default.</summary>
    private static bool Compiles(XamlMarkupExtensionValue binding, XamlElement element, bool byDefault)
    {
        switch (binding.TypeName.LocalName)
        {
            case "CompiledBinding" or "CompiledBindingExtension":
                return true;
            case "ReflectionBinding" or "ReflectionBindingExtension":
                return false;
        }

        foreach (XamlElement scope in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            if (scope.GetDirective(XamlDirectives.CompileBindings) is { } written && bool.TryParse(written.Trim(), out bool compiled))
            {
                return compiled;
            }
        }

        return byDefault;
    }

    /// <summary>The element that owns another in the document: its parent, past a property element.</summary>
    private static XamlElement? Owner(XamlElement element)
    {
        for (XamlSyntaxNode? parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XamlElement { IsPropertyElementSyntax: false } owner)
            {
                return owner;
            }
        }

        return null;
    }

    /// <summary>The nearest template an element stands in — its own name scope; <see langword="null"/> outside templates.</summary>
    private static XamlElement? Template(XamlElement element) =>
        element.AncestorsAndSelf().OfType<XamlElement>().Skip(1).FirstOrDefault(IsTemplate);

    private static bool IsTemplate(XamlElement element) =>
        !element.IsPropertyElementSyntax && element.Name.LocalName.EndsWith("Template", StringComparison.Ordinal);

    /// <summary>What a binding says about its source, read from the markup alone.</summary>
    /// <param name="Source">What it reads from.</param>
    /// <param name="Path">The path read against the source, a leading <c>!</c> kept.</param>
    /// <param name="Name">The element's name or the ancestor's type, as written; <see langword="null"/> otherwise.</param>
    /// <param name="Level">Which ancestor of the type, counting from the nearest at one.</param>
    private sealed record WrittenBinding(XamlBindingSource Source, string Path, string? Name = null, int Level = 1)
    {
        public static WrittenBinding Read(XamlMarkupExtensionValue binding)
        {
            string path = (binding.PositionalArguments.FirstOrDefault() ?? binding.GetArgument("Path"))?.Value is XamlLiteralValue { Text: { } text }
                ? text.Trim()
                : string.Empty;

            if (binding.GetArgument("Source") is not null)
            {
                return new WrittenBinding(XamlBindingSource.Other, path);
            }

            if (binding.GetArgument("RelativeSource")?.Value is { } relative)
            {
                return Relative(relative, path);
            }

            if (binding.GetArgument("ElementName")?.Value is XamlLiteralValue { Text: { Length: > 0 } elementName })
            {
                return new WrittenBinding(XamlBindingSource.Element, path, elementName.Trim());
            }

            // A leading negation belongs to the value, wherever the path reads it from.
            string negation = new([.. path.TakeWhile(static character => character == '!')]);
            string rest = path[negation.Length..].TrimStart();

            if (rest.StartsWith('#'))
            {
                int end = rest.IndexOfAny(['.', '['], 1);
                string name = end < 0 ? rest[1..] : rest[1..end];

                return new WrittenBinding(XamlBindingSource.Element, negation + After(rest, end), name);
            }

            if (rest.StartsWith("$self", StringComparison.Ordinal))
            {
                return new WrittenBinding(XamlBindingSource.Self, negation + After(rest, "$self".Length));
            }

            if (rest.StartsWith("$parent", StringComparison.Ordinal))
            {
                int at = "$parent".Length;
                string? type = null;
                int level = 1;

                if (at < rest.Length && rest[at] == '[' && rest.IndexOf(']', at) is var close and > 0)
                {
                    string[] parts = rest[(at + 1)..close].Split(';');

                    type = parts[0].Trim() is { Length: > 0 } named ? named : null;

                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int written))
                    {
                        level = written;
                    }

                    at = close + 1;
                }

                return new WrittenBinding(XamlBindingSource.Ancestor, negation + After(rest, at), type, Math.Max(1, level));
            }

            return new WrittenBinding(XamlBindingSource.DataContext, path);
        }

        /// <summary>A <c>RelativeSource</c>: itself, an ancestor, the data context — or something else.</summary>
        private static WrittenBinding Relative(XamlValue relative, string path)
        {
            if (relative is not XamlMarkupExtensionValue { TypeName.LocalName: "RelativeSource" or "RelativeSourceExtension" } source)
            {
                return new WrittenBinding(XamlBindingSource.Other, path);
            }

            string? mode = (source.PositionalArguments.FirstOrDefault() ?? source.GetArgument("Mode"))?.Value is XamlLiteralValue { Text: { } text }
                ? text.Trim()
                : null;
            string? type = source.GetArgument("AncestorType")?.Value switch
            {
                XamlLiteralValue literal => literal.Text.Trim(),
                XamlMarkupExtensionValue extension => extension.ToXamlText(),
                _ => null,
            };
            int level = source.GetArgument("AncestorLevel")?.Value is XamlLiteralValue { Text: { } written } &&
                        int.TryParse(written.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 1;

            // Naming the ancestor's type is asking for an ancestor, whatever the mode says — Avalonia reads it so.
            if (type is not null || string.Equals(mode, "FindAncestor", StringComparison.Ordinal))
            {
                return new WrittenBinding(XamlBindingSource.Ancestor, path, type, Math.Max(1, level));
            }

            return mode switch
            {
                "Self" => new WrittenBinding(XamlBindingSource.Self, path),
                null or "DataContext" => new WrittenBinding(XamlBindingSource.DataContext, path),
                _ => new WrittenBinding(XamlBindingSource.Other, path),
            };
        }

        /// <summary>What a path says after its source: past the source and the dot after it.</summary>
        private static string After(string path, int end)
        {
            if (end < 0 || end >= path.Length)
            {
                return string.Empty;
            }

            return path[end] == '.' ? path[(end + 1)..] : path[end..];
        }
    }
}
