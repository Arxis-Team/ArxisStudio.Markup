using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a change can be written on an object where it stands: a value taken out, a value set, a
/// binding or a resource reference bound.
/// </summary>
/// <remarks>
/// Worked out before anything is written — a static member's type is resolved through the
/// environment, a binding's arguments are read and converted — so a change that turns out not to be
/// writable in place is rebuilt instead, rather than refused half-way.
/// </remarks>
internal abstract class XamlInPlaceWrite
{
    /// <summary>Takes the local value out, so whatever it covered shows again.</summary>
    internal static XamlInPlaceWrite Clear { get; } = new Clearing();

    /// <summary>Writes the change onto the object.</summary>
    /// <param name="target">The object.</param>
    /// <param name="member">The member the change names on it.</param>
    internal abstract void Apply(object target, XamlMemberDescriptor member);

    /// <summary>Ends a binding the document had written on the property, which a new value replaces.</summary>
    /// <remarks>
    /// A binding the document applies runs at local-value priority, and neither setting nor binding a
    /// new local value is guaranteed to end it: the next change of its source would write over what
    /// the document now says.
    /// </remarks>
    private protected static void EndBinding(object target, XamlMemberDescriptor member)
    {
        if (member.AvaloniaProperty is { } property && target is AvaloniaObject bound)
        {
            BindingOperations.GetBindingExpressionBase(bound, property)?.Dispose();
        }
    }

    /// <summary>A value: a static member's, or none.</summary>
    internal sealed class Setting(object? value) : XamlInPlaceWrite
    {
        internal object? Value { get; } = value;

        internal override void Apply(object target, XamlMemberDescriptor member)
        {
            EndBinding(target, member);
            XamlDesignValues.Write(target, member, Value);
        }
    }

    /// <summary>A binding — to a source, or to a resource that may change.</summary>
    internal sealed class Binding(BindingBase binding) : XamlInPlaceWrite
    {
        internal override void Apply(object target, XamlMemberDescriptor member)
        {
            EndBinding(target, member);
            ((AvaloniaObject)target).Bind(member.AvaloniaProperty!, binding);
        }
    }

    private sealed class Clearing : XamlInPlaceWrite
    {
        internal override void Apply(object target, XamlMemberDescriptor member)
        {
            EndBinding(target, member);
            ((AvaloniaObject)target).ClearValue(member.AvaloniaProperty!);
        }
    }
}

/// <summary>
/// Reads the expressions an update can set on an object without building anything.
/// </summary>
/// <remarks>
/// <para>
/// A binding, a dynamic resource, a static member and a null. Each is something a load would turn
/// into a value or a binding with nothing else to go on but the element it is written on — no
/// dictionary read at build time, no object constructed — so setting it where the property stands
/// is the same as building the element with it, and costs an inspector's edit nothing but the edit.
/// </para>
/// <para>
/// Everything else is left to a rebuild: a static resource, read once from the dictionaries in
/// scope while the element is built; a converter, which is a resource; an argument this does not
/// know. A rebuild is always right, and an in-place write that guessed would not be.
/// </para>
/// </remarks>
internal static class XamlInPlaceValues
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>Reports whether an expression is of a kind that may be set in place, from its syntax alone.</summary>
    /// <param name="extension">The expression.</param>
    /// <param name="element">The element it is written on, whose namespaces name its type.</param>
    /// <returns><see langword="true"/> when it is worth asking <see cref="EvaluateAsync"/> about.</returns>
    internal static bool IsCandidate(XamlMarkupExtensionValue extension, XamlElement element) =>
        KindOf(extension, element) is not Kind.None;

    /// <summary>Works out what an expression writes on a member, or that only a rebuild can say.</summary>
    /// <param name="extension">The expression.</param>
    /// <param name="element">The element it is written on.</param>
    /// <param name="member">The member it is written to, as the object's type has it.</param>
    /// <param name="environment">The environment a static member's type is resolved through.</param>
    /// <param name="compiledByDefault">
    /// Whether a <c>{Binding}</c> the document says nothing about compiles, as the session's load
    /// was told.
    /// </param>
    /// <param name="cancellationToken">A token to observe while resolving.</param>
    /// <returns>The write, or <see langword="null"/> when the element has to be rebuilt instead.</returns>
    internal static async ValueTask<XamlInPlaceWrite?> EvaluateAsync(
        XamlMarkupExtensionValue extension,
        XamlElement element,
        XamlMemberDescriptor member,
        XamlLoadEnvironment environment,
        bool compiledByDefault,
        CancellationToken cancellationToken)
    {
        switch (KindOf(extension, element))
        {
            case Kind.Null:
                return Fits(member, null) && extension.Arguments.IsEmpty ? new XamlInPlaceWrite.Setting(null) : null;

            case Kind.Static:
                return await StaticAsync(extension, element, member, environment, cancellationToken).ConfigureAwait(false);

            case Kind.DynamicResource:
                return member.AvaloniaProperty is not null && ResourceKey(extension) is { } key
                    ? new XamlInPlaceWrite.Binding(new DynamicResourceExtension(key))
                    : null;

            case Kind.Binding:
                return member.AvaloniaProperty is not null
                    && !Compiles(extension, element, compiledByDefault)
                    && await BindingAsync(extension, element, member, environment, cancellationToken)
                        .ConfigureAwait(false) is { } binding
                    ? new XamlInPlaceWrite.Binding(binding)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>Reports whether a load would compile a binding written on an element.</summary>
    /// <remarks>
    /// A compiled binding is checked against its data type when it is built, and a load refuses a
    /// path the type does not have; a reflection binding set in its place would take the path and
    /// show nothing. So where bindings compile, only a rebuild says what the document means — the
    /// nearest <c>x:CompileBindings</c> decides, and the session's own default where none is written.
    /// <c>{ReflectionBinding}</c> says for itself that it does not compile.
    /// </remarks>
    private static bool Compiles(XamlMarkupExtensionValue extension, XamlElement element, bool compiledByDefault)
    {
        if (extension.TypeName.LocalName is not "Binding")
        {
            return false;
        }

        foreach (XamlElement scope in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            if (scope.GetDirective(XamlDirectives.CompileBindings) is { } written)
            {
                // Text that is not a truth value is a load's to report, and a rebuild is the load.
                return !bool.TryParse(written, out bool compiled) || compiled;
            }
        }

        return compiledByDefault;
    }

    private static async ValueTask<XamlInPlaceWrite?> StaticAsync(
        XamlMarkupExtensionValue extension,
        XamlElement element,
        XamlMemberDescriptor member,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (Single(extension, "Member") is not XamlLiteralValue { Text: { } written })
        {
            return null;
        }

        int dot = written.LastIndexOf('.');

        if (dot <= 0
            || await TypeAsync(written[..dot], element, environment, cancellationToken).ConfigureAwait(false)
                is not { } owner)
        {
            return null;
        }

        string name = written[(dot + 1)..];
        const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        object? value;

        try
        {
            if (owner.GetField(name, Statics) is { } field)
            {
                value = field.GetValue(null);
            }
            else if (owner.GetProperty(name, Statics) is { GetMethod: not null } property)
            {
                value = property.GetValue(null);
            }
            else
            {
                return null;
            }
        }
        catch (TargetInvocationException)
        {
            // The member's getter is the author's code; one that throws is a rebuild's to report.
            return null;
        }

        return Fits(member, value) ? new XamlInPlaceWrite.Setting(value) : null;
    }

    private static async ValueTask<BindingBase?> BindingAsync(
        XamlMarkupExtensionValue extension,
        XamlElement element,
        XamlMemberDescriptor member,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        var binding = new ReflectionBinding();
        int position = 0;

        foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
        {
            string name = argument.Name ?? (position++ == 0 ? "Path" : string.Empty);

            switch (name, argument.Value)
            {
                case ("Path", XamlLiteralValue path):
                    binding.Path = path.Text;
                    break;

                case ("Mode", XamlLiteralValue mode) when Enum.TryParse(mode.Text, out BindingMode parsed):
                    binding.Mode = parsed;
                    break;

                case ("StringFormat", XamlLiteralValue format):
                    // "{}" is how a format that starts with a brace says it is not an expression.
                    binding.StringFormat = format.Text.StartsWith("{}", StringComparison.Ordinal)
                        ? format.Text[2..]
                        : format.Text;
                    break;

                case ("ElementName", XamlLiteralValue elementName):
                    binding.ElementName = elementName.Text;
                    break;

                case ("FallbackValue", XamlValue fallback) when Literal(fallback, member) is (true, var value):
                    binding.FallbackValue = value;
                    break;

                case ("TargetNullValue", XamlValue nullValue) when Literal(nullValue, member) is (true, var value):
                    binding.TargetNullValue = value;
                    break;

                case ("RelativeSource", XamlMarkupExtensionValue relative):
                    if (await RelativeSourceAsync(relative, element, environment, cancellationToken)
                            .ConfigureAwait(false) is not { } source)
                    {
                        return null;
                    }

                    binding.RelativeSource = source;
                    break;

                default:
                    // A converter, a source, a delay, a priority, an argument nobody knows: whatever
                    // a load would make of it, a rebuild makes the same, and this would be guessing.
                    return null;
            }
        }

        return binding;
    }

    private static async ValueTask<RelativeSource?> RelativeSourceAsync(
        XamlMarkupExtensionValue relative,
        XamlElement element,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(relative.TypeName.LocalName, "RelativeSource", StringComparison.Ordinal))
        {
            return null;
        }

        var source = new RelativeSource();
        int position = 0;

        foreach (XamlMarkupExtensionArgument argument in relative.Arguments)
        {
            string name = argument.Name ?? (position++ == 0 ? "Mode" : string.Empty);

            switch (name, argument.Value)
            {
                case ("Mode", XamlLiteralValue mode) when Enum.TryParse(mode.Text, out RelativeSourceMode parsed):
                    source.Mode = parsed;
                    break;

                case ("AncestorLevel", XamlLiteralValue level) when int.TryParse(level.Text, out int parsed):
                    source.AncestorLevel = parsed;
                    break;

                case ("AncestorType", XamlValue type):
                    string? written = type switch
                    {
                        XamlLiteralValue literal => literal.Text,
                        XamlMarkupExtensionValue { TypeName.LocalName: "Type" } named => Single(named, "TypeName") is XamlLiteralValue inner ? inner.Text : null,
                        _ => null,
                    };

                    if (written is null
                        || await TypeAsync(written, element, environment, cancellationToken).ConfigureAwait(false)
                            is not { } ancestor)
                    {
                        return null;
                    }

                    source.AncestorType = ancestor;

                    // Naming the ancestor's type is asking for an ancestor, whether or not the mode
                    // was written, which is how Avalonia reads it too.
                    source.Mode = RelativeSourceMode.FindAncestor;
                    break;

                default:
                    return null;
            }
        }

        return source;
    }

    /// <summary>Gets a resource key written as text, the only kind a dictionary is looked up by here.</summary>
    private static string? ResourceKey(XamlMarkupExtensionValue extension) =>
        Single(extension, "ResourceKey") is XamlLiteralValue { Text.Length: > 0 } key ? key.Text : null;

    /// <summary>Gets an extension's one argument, written by position or under its name.</summary>
    private static XamlValue? Single(XamlMarkupExtensionValue extension, string name) =>
        extension.Arguments.Length == 1
        && (extension.Arguments[0].IsPositional
            || string.Equals(extension.Arguments[0].Name, name, StringComparison.Ordinal))
            ? extension.Arguments[0].Value
            : null;

    /// <summary>Converts a binding's literal argument to what the member holds.</summary>
    private static (bool Converted, object? Value) Literal(XamlValue written, XamlMemberDescriptor member)
    {
        if (written is XamlMarkupExtensionValue { TypeName.LocalName: "Null" or "NullExtension" })
        {
            return (true, null);
        }

        if (written is not XamlLiteralValue literal)
        {
            return (false, null);
        }

        XamlValueConversionResult converted = member.ConvertFromText(literal.Text);

        return converted.Succeeded ? (true, converted.Value) : (false, null);
    }

    /// <summary>Reports whether a value can be written to a member as it is.</summary>
    private static bool Fits(XamlMemberDescriptor member, object? value)
    {
        if (member.IsReadOnly || !member.CanWrite)
        {
            return false;
        }

        Type type = member.ValueType;

        return value is null
            ? !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
            : type.IsInstanceOfType(value);
    }

    /// <summary>Resolves a type a document names as <c>prefix:Name</c>, where the element sits.</summary>
    private static async ValueTask<Type?> TypeAsync(
        string written,
        XamlElement element,
        XamlLoadEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (written.Contains('.', StringComparison.Ordinal) || written.Contains('+', StringComparison.Ordinal))
        {
            // A nested type or a namespace-qualified name is not something a document names by prefix.
            return null;
        }

        XamlQualifiedName name = XamlQualifiedName.Parse(written);

        if (element.NamespaceContext.LookupNamespace(name.Prefix) is not { } namespaceUri)
        {
            return null;
        }

        XamlTypeResolution resolved = await environment.TypeResolver
            .ResolveAsync(new XamlTypeName(namespaceUri, name.LocalName), element.NamespaceContext, cancellationToken)
            .ConfigureAwait(false);

        return resolved.Type;
    }

    private static Kind KindOf(XamlMarkupExtensionValue extension, XamlElement element)
    {
        string? namespaceUri = element.NamespaceContext.LookupNamespace(extension.TypeName.Prefix);
        string name = extension.TypeName.LocalName;

        if (string.Equals(namespaceUri, XamlNamespaces.Xaml, StringComparison.Ordinal))
        {
            return name switch
            {
                "Null" or "NullExtension" => Kind.Null,
                "Static" or "StaticExtension" => Kind.Static,
                _ => Kind.None,
            };
        }

        if (string.Equals(namespaceUri, AvaloniaNamespace, StringComparison.Ordinal))
        {
            return name switch
            {
                "Binding" or "ReflectionBinding" => Kind.Binding,
                "DynamicResource" or "DynamicResourceExtension" => Kind.DynamicResource,
                _ => Kind.None,
            };
        }

        return Kind.None;
    }

    private enum Kind
    {
        None,
        Null,
        Static,
        DynamicResource,
        Binding,
    }
}
