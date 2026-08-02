using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Works out what a member name means on a given type.
/// </summary>
/// <remarks>
/// <para>
/// This is the classification the syntax package refuses to make, because making it needs CLR
/// metadata. <c>Grid.Row</c> has the same shape whether it is an attached property, a nested
/// property element or a plain CLR property whose name happens to contain a dot; only the type
/// system can say which.
/// </para>
/// <para>
/// Results are cached per resolver: a document asks about the same handful of members over and
/// over, and reflection over a type's whole surface is not cheap.
/// </para>
/// </remarks>
public sealed class XamlMemberResolver
{
    private readonly ConcurrentDictionary<(Type Target, string Name), XamlMemberDescriptor> _cache = new();

    /// <summary>Gets the shared instance.</summary>
    /// <remarks>
    /// For a caller with no <see cref="XamlLoadEnvironment"/> to hand. A session uses its
    /// environment's resolver instead, so that what is cached about an assembly's types is
    /// discarded with the environment that resolved them rather than held for the life of the
    /// process — see <see cref="XamlLoadEnvironment.MemberResolver"/>.
    /// </remarks>
    public static XamlMemberResolver Instance { get; } = new();

    /// <summary>
    /// Lists the members of a type that a document can set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia's property system is what this reads, in the three shapes it has, plus the CLR
    /// properties around it:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Styled and direct properties</b> — everything <c>GetRegistered</c> answers for the type
    /// and its bases. A direct property is registered like any other and is told apart by
    /// <see cref="XamlMemberKind.DirectProperty"/>; the difference matters to a tool because a
    /// direct property has no styling and can be read-only in a way a styled one is not.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Attached properties</b> — <c>GetRegisteredAttached</c>, which answers by the type the
    /// property attaches <i>to</i>. They are named <c>Owner.Member</c>, because that is how a
    /// document writes them, and a list built from simple names alone would contain none of them.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Public CLR properties</b> — what a plain object exposes, and what a control adds beyond
    /// its registered properties. A collection-valued one is <see cref="XamlMemberKind.Collection"/>
    /// and the one carrying <c>[Content]</c> is <see cref="XamlMemberKind.Content"/>, because both
    /// change what writing to them means.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// A property registered both ways — <c>KeyboardNavigation.IsTabStop</c> is also an ordinary
    /// property of every control — is listed once, under its simple name. Both spellings are
    /// valid XAML, but they are one property, and offering it twice invites a panel to show two
    /// rows that write to the same value.
    /// </para>
    /// <para>
    /// Which of them are worth showing is still the tool's decision — a hundred inherited
    /// properties is a correct answer and a useless panel. What is answered here is which exist
    /// and what each one is.
    /// </para>
    /// <para>
    /// The answer can grow between calls, and deliberately is not cached as a whole. Avalonia
    /// registers an attached property in the static constructor of the type that declares it, so
    /// <c>Grid.Row</c> exists as a member of every control only once something has caused
    /// <c>Grid</c> to be initialised. A tool that resolves types as documents ask for them will
    /// see that happen while it is running, and a list answered from a cache taken beforehand
    /// would never catch up. The descriptors themselves still come from the shared cache.
    /// </para>
    /// </remarks>
    /// <param name="targetType">The type to list.</param>
    /// <returns>The members, ordered by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is <see langword="null"/>.</exception>
    public ImmutableArray<XamlMemberDescriptor> Enumerate(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        return Discover(targetType);
    }

    private ImmutableArray<XamlMemberDescriptor> Discover(Type targetType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<XamlMemberDescriptor>();

        if (typeof(AvaloniaObject).IsAssignableFrom(targetType))
        {
            // Styled and direct properties of the type and its bases, under the simple name a
            // document writes them with.
            IReadOnlyList<AvaloniaProperty> registered =
                AvaloniaPropertyRegistry.Instance.GetRegistered(targetType);

            var known = new HashSet<AvaloniaProperty>(registered);

            foreach (AvaloniaProperty property in registered)
            {
                if (names.Add(property.Name))
                {
                    members.Add(Resolve(targetType, property));
                }
            }

            // Attached properties answer to the type they attach to rather than to the one that
            // declares them, and are written Owner.Member — so a list built from simple names
            // alone would contain none of them.
            foreach (AvaloniaProperty property in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(targetType))
            {
                // Registered both ways: the same property, already listed under the name a
                // document is more likely to write it with.
                if (known.Contains(property))
                {
                    continue;
                }

                string name = $"{property.OwnerType.Name}.{property.Name}";

                if (names.Add(name))
                {
                    members.Add(Resolve(targetType, name, property.OwnerType));
                }
            }
        }

        foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0 && names.Add(property.Name))
            {
                members.Add(Resolve(targetType, property.Name));
            }
        }

        return [.. members.OrderBy(static member => member.Name, StringComparer.Ordinal)];
    }

    /// <summary>Resolves a member name against a type.</summary>
    /// <param name="targetType">The type the member is written on.</param>
    /// <param name="name">The member name, which may have the <c>Owner.Member</c> shape.</param>
    /// <param name="ownerType">
    /// The owner of an attached member, when the caller has already resolved it. Without it,
    /// an <c>Owner.Member</c> name is matched against attached properties registered under any
    /// owner of that simple name.
    /// </param>
    /// <returns>What the member is, or an unresolved descriptor when nothing matched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    public XamlMemberDescriptor Resolve(Type targetType, string name, Type? ownerType = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(name);

        if (ownerType is not null)
        {
            return ResolveCore(targetType, name, ownerType);
        }

        return _cache.GetOrAdd((targetType, name), key => ResolveCore(key.Target, key.Name, null));
    }

    /// <summary>Resolves the member an Avalonia property is.</summary>
    /// <param name="targetType">The type the property is being used on.</param>
    /// <param name="property">The property.</param>
    /// <returns>What the property is.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
    public XamlMemberDescriptor Resolve(Type targetType, AvaloniaProperty property)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(property);

        // Routed through the cache like every other lookup, so a property asked about
        // repeatedly costs one reflection pass rather than one per call.
        return _cache.GetOrAdd(
            (targetType, property.Name), _ => Describe(targetType, property, property.Name));
    }

    private static XamlMemberDescriptor ResolveCore(Type targetType, string name, Type? ownerType)
    {
        int dot = name.IndexOf('.', StringComparison.Ordinal);

        if (dot >= 0)
        {
            return ResolveAttached(targetType, name[..dot], name[(dot + 1)..], name, ownerType);
        }

        if (AvaloniaPropertyRegistry.Instance.FindRegistered(targetType, name) is { } avaloniaProperty)
        {
            return Describe(targetType, avaloniaProperty, name);
        }

        if (targetType.GetEvent(name, BindingFlags.Public | BindingFlags.Instance) is { } clrEvent)
        {
            return new XamlMemberDescriptor(
                name, XamlMemberKind.Event, clrEvent.DeclaringType ?? targetType, targetType,
                clrEvent.EventHandlerType ?? typeof(Delegate),
                canRead: false, canWrite: true, isAttached: false, isReadOnly: false)
            {
                Event = clrEvent,
            };
        }

        if (targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } clrProperty)
        {
            return DescribeClr(targetType, clrProperty, name);
        }

        return Unresolved(targetType, name);
    }

    /// <summary>Resolves an <c>Owner.Member</c> name to an attached property or accessor pair.</summary>
    private static XamlMemberDescriptor ResolveAttached(
        Type targetType,
        string ownerName,
        string memberName,
        string name,
        Type? ownerType)
    {
        AvaloniaProperty? attached = AvaloniaPropertyRegistry.Instance
            .GetRegisteredAttached(targetType)
            .FirstOrDefault(property =>
                string.Equals(property.Name, memberName, StringComparison.Ordinal)
                && (ownerType is null
                    ? string.Equals(property.OwnerType.Name, ownerName, StringComparison.Ordinal)
                    : property.OwnerType == ownerType));

        if (attached is not null)
        {
            return Describe(targetType, attached, name);
        }

        // Not every attached member is an AvaloniaProperty. The static Get/Set accessor pair is
        // a legitimate way to write one, and a document using it looks identical.
        if (ownerType is not null)
        {
            MethodInfo? getter = ownerType.GetMethod(
                "Get" + memberName, BindingFlags.Public | BindingFlags.Static);
            MethodInfo? setter = ownerType.GetMethod(
                "Set" + memberName, BindingFlags.Public | BindingFlags.Static);

            if (getter is not null || setter is not null)
            {
                Type valueType = getter?.ReturnType
                    ?? setter?.GetParameters().ElementAtOrDefault(1)?.ParameterType
                    ?? typeof(object);

                return new XamlMemberDescriptor(
                    name, XamlMemberKind.AttachedProperty, ownerType, targetType, valueType,
                    canRead: getter is not null, canWrite: setter is not null,
                    isAttached: true, isReadOnly: setter is null)
                {
                    AttachedAccessors = (getter, setter),
                };
            }
        }

        return Unresolved(targetType, name);
    }

    private static XamlMemberDescriptor Describe(Type targetType, AvaloniaProperty property, string name)
    {
        // The content property is reported by its role, not by the mechanism behind it. Whether
        // unnamed children land there is what an edit has to know; that it also happens to be a
        // StyledProperty is still available through AvaloniaProperty.
        XamlMemberKind kind = IsContentProperty(targetType, property.Name)
            ? XamlMemberKind.Content
            : property switch
            {
                { IsAttached: true } => XamlMemberKind.AttachedProperty,
                { IsDirect: true } => XamlMemberKind.DirectProperty,
                _ => XamlMemberKind.StyledProperty,
            };

        return new XamlMemberDescriptor(
            name, kind, property.OwnerType, targetType, property.PropertyType,
            canRead: true, canWrite: !property.IsReadOnly,
            isAttached: property.IsAttached, isReadOnly: property.IsReadOnly)
        {
            AvaloniaProperty = property,
        };
    }

    /// <summary>Determines whether a name is the type's content property.</summary>
    private static bool IsContentProperty(Type targetType, string name) =>
        targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?.GetCustomAttribute<ContentAttribute>(inherit: true) is not null;

    private static XamlMemberDescriptor DescribeClr(Type targetType, PropertyInfo property, string name)
    {
        // The content property is where unnamed children go, and a collection-valued member is
        // where they are added rather than assigned. Both change what an edit means, so they
        // are distinguished from an ordinary property.
        bool isContent = property.GetCustomAttribute<ContentAttribute>(inherit: true) is not null;

        bool isCollection = !property.CanWrite
            && property.PropertyType != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);

        XamlMemberKind kind = isContent
            ? XamlMemberKind.Content
            : isCollection ? XamlMemberKind.Collection : XamlMemberKind.ClrProperty;

        return new XamlMemberDescriptor(
            name, kind, property.DeclaringType ?? targetType, targetType, property.PropertyType,
            canRead: property.CanRead, canWrite: property.CanWrite,
            isAttached: false, isReadOnly: !property.CanWrite)
        {
            ClrProperty = property,
        };
    }

    private static XamlMemberDescriptor Unresolved(Type targetType, string name) =>
        new(name, XamlMemberKind.Unknown, targetType, targetType, typeof(object),
            canRead: false, canWrite: false, isAttached: false, isReadOnly: true);
}
