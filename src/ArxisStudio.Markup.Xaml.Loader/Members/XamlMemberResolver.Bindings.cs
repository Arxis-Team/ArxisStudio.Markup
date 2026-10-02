using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The half of the resolver that answers what a binding can read from its source.
/// </summary>
/// <remarks>
/// <para>
/// A binding's source is data — a view model, a model — and what a binding can name on it is its
/// public instance properties, which is what Avalonia's reflection bindings read and what its compiled
/// bindings are checked against. That is a different list from <see cref="Enumerate"/>, which answers
/// what a document can <em>set</em> on an object it builds.
/// </para>
/// <para>
/// Paths are read the way a binding reads them, as far as that can be done from types alone: dotted
/// member names, integer and string indexers, and the negation a leading <c>!</c> writes. A step whose
/// type is <see cref="object"/> or is resolved at run time is as far as types go, and so is anything
/// that names a source other than the data context — an attached property in parentheses, a cast,
/// <c>$parent</c>, <c>#name</c>; those read as <see cref="XamlBindingPathStatus.NotUnderstood"/>,
/// never as broken.
/// </para>
/// </remarks>
public sealed partial class XamlMemberResolver
{
    private readonly ConcurrentDictionary<Type, ImmutableArray<XamlBindableMember>> _bindable = new();

    /// <summary>
    /// The properties a binding can read on each type asked about, kept with the environment like
    /// every other answer here: a tool checks every binding of a form against them on every update.
    /// </summary>
    private readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _readable = new();

    /// <summary>Lists what a binding can name on a source of a type.</summary>
    /// <remarks>
    /// Public instance properties with a public getter and no index, each once: a property a derived
    /// type hides with one of its own name is listed as the derived type declares it. An interface's
    /// list includes what the interfaces it extends declare, because a binding against it reads them.
    /// </remarks>
    /// <param name="sourceType">The type of the binding's source.</param>
    /// <returns>The members, ordered by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceType"/> is <see langword="null"/>.</exception>
    public ImmutableArray<XamlBindableMember> EnumerateBindable(Type sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);

        return _bindable.GetOrAdd(sourceType, type =>
        [
            .. Readable(type)
                .Select(static property => new XamlBindableMember(
                    property.Name,
                    DisplayName(property.PropertyType),
                    property.SetMethod is { IsPublic: true },
                    IsCollection(property.PropertyType),
                    typeof(ICommand).IsAssignableFrom(property.PropertyType)))
                .OrderBy(static member => member.Name, StringComparer.Ordinal),
        ]);
    }

    /// <summary>Reads a binding path against the type of its source.</summary>
    /// <param name="sourceType">The type of the binding's source — the data type in scope, or the design data's.</param>
    /// <param name="path">The path as a binding writes it: <c>Customer.Name</c>, <c>Items[0]</c>, <c>!IsBusy</c>.</param>
    /// <returns>Where the path ends, where it breaks, or where reading it stopped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceType"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    public XamlBindingPathResult ResolveBindingPath(Type sourceType, string path)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(path);

        string written = path.Trim();
        bool negated = false;

        while (written.StartsWith('!'))
        {
            negated = true;
            written = written[1..].TrimStart();
        }

        if (written.IndexOfAny(['(', ')', '$', '#', '^', '/']) >= 0)
        {
            return NotUnderstood(written, "The path names a source or a member this reading does not follow.");
        }

        Type current = sourceType;

        if (written.Length > 0 && written != ".")
        {
            foreach (string step in written.Split('.'))
            {
                if (RunTimeOnly(current))
                {
                    return NotUnderstood(step, $"'{DisplayName(current)}' says what it holds only when the binding runs.");
                }

                XamlBindingPathResult next = Step(current, step.Trim());

                if (next.Status != XamlBindingPathStatus.Resolved)
                {
                    return next;
                }

                current = next.ResultType!;
            }
        }

        return new XamlBindingPathResult
        {
            Status = XamlBindingPathStatus.Resolved,
            ResultType = negated ? typeof(bool) : current,
        };
    }

    /// <summary>Follows one step of a path: a member, with any indexers after it.</summary>
    private XamlBindingPathResult Step(Type type, string step)
    {
        int bracket = step.IndexOf('[', StringComparison.Ordinal);
        string name = (bracket < 0 ? step : step[..bracket]).Trim();
        Type current = type;

        // An indexer straight on the source, as "[0]" is written, has no member before it.
        if (name.Length > 0)
        {
            if (Readable(type).FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal))
                is not { } property)
            {
                return Broken(name, type);
            }

            current = property.PropertyType;
        }

        string rest = bracket < 0 ? string.Empty : step[bracket..];

        while (rest.Length > 0)
        {
            int close = rest.IndexOf(']', StringComparison.Ordinal);

            if (!rest.StartsWith('[') || close < 0)
            {
                return NotUnderstood(step, "The step's indexer is not written as '[...]'.");
            }

            if (RunTimeOnly(current))
            {
                return NotUnderstood(step, $"'{DisplayName(current)}' says what it holds only when the binding runs.");
            }

            if (Indexed(current, rest[1..close].Trim()) is not { } element)
            {
                return NotUnderstood(step, $"'{DisplayName(current)}' has no indexer this reading follows.");
            }

            current = element;
            rest = rest[(close + 1)..];
        }

        return new XamlBindingPathResult { Status = XamlBindingPathStatus.Resolved, ResultType = current };
    }

    /// <summary>Finds what an indexer with this argument reads from a type, or nothing when it has none that takes it.</summary>
    private static Type? Indexed(Type type, string argument)
    {
        bool integer = int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        if (integer && type.IsArray)
        {
            return type.GetElementType();
        }

        Type key = integer ? typeof(int) : typeof(string);

        foreach (Type candidate in SelfAndInterfaces(type))
        {
            foreach (PropertyInfo property in candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters() is [{ ParameterType: { } parameter }] && parameter == key)
                {
                    return property.PropertyType;
                }
            }
        }

        return null;
    }

    /// <summary>Reports whether a type's members are only known when a binding runs.</summary>
    private static bool RunTimeOnly(Type type) =>
        type == typeof(object)
        || typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type)
        || typeof(ICustomTypeDescriptor).IsAssignableFrom(type);

    /// <summary>
    /// Gets the public instance properties a binding can read, each name once, the most derived
    /// declaration winning.
    /// </summary>
    private ImmutableArray<PropertyInfo> Readable(Type type) =>
        _readable.GetOrAdd(type, static source =>
        [
            .. SelfAndInterfaces(source)
                .SelectMany(static candidate => candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .Where(static property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
                .GroupBy(static property => property.Name, StringComparer.Ordinal)
                .Select(static group => group.OrderByDescending(static property => Depth(property.DeclaringType)).First()),
        ]);

    /// <summary>
    /// Gets a type and, for an interface, the interfaces it extends — which reflection does not
    /// include in an interface's own properties.
    /// </summary>
    private static IEnumerable<Type> SelfAndInterfaces(Type type) =>
        type.IsInterface ? [type, .. type.GetInterfaces()] : [type];

    /// <summary>How far a type is from the root of its hierarchy, so a hiding declaration outranks the hidden one.</summary>
    private static int Depth(Type? type)
    {
        int depth = 0;

        for (Type? current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>Writes a type's name the way a reader writes it in C#.</summary>
    private static string DisplayName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return DisplayName(underlying) + "?";
        }

        if (type.IsArray)
        {
            return DisplayName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (Keyword(type) is { } keyword)
        {
            return keyword;
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);

        return (tick < 0 ? name : name[..tick])
            + "<" + string.Join(", ", type.GetGenericArguments().Select(DisplayName)) + ">";
    }

    private static string? Keyword(Type type) =>
        type.IsEnum ? null : Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Char => "char",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.String => "string",
            _ => type == typeof(object) ? "object" : null,
        };

    private static XamlBindingPathResult Broken(string step, Type type) =>
        new()
        {
            Status = XamlBindingPathStatus.Broken,
            Step = step,
            Message = $"'{DisplayName(type)}' has no public property '{step}' a binding can read.",
        };

    private static XamlBindingPathResult NotUnderstood(string step, string message) =>
        new()
        {
            Status = XamlBindingPathStatus.NotUnderstood,
            Step = step,
            Message = message,
        };
}
