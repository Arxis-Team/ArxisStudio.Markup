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
public sealed partial class XamlMemberResolver
{
    private readonly ConcurrentDictionary<(Type Target, string Name), XamlMemberDescriptor> _cache = new();
    private readonly ConcurrentDictionary<Type, XamlMemberDescriptor?> _content = new();

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
    /// rows that write to the same value. Once, but under the name a document can write: an attached
    /// property the type answers for and does not wrap — <c>InputElement.IsHoldWithMouseEnabled</c> —
    /// is listed by its owner's name, because the XAML compiler resolves the simple one to nothing.
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

        // Walked from the type down to object rather than asked of the type as a whole, because
        // GetProperties answers with both declarations where one shadows another and the derived
        // one is the one a document means.
        for (Type? declaring = targetType; declaring is not null; declaring = declaring.BaseType)
        {
            const BindingFlags Declared =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (PropertyInfo property in declaring.GetProperties(Declared))
            {
                if (property.GetIndexParameters().Length == 0 && names.Add(property.Name))
                {
                    members.Add(Resolve(targetType, property.Name));
                }
            }
        }

        return [.. members.OrderBy(static member => member.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Finds the member that unnamed children of a type belong to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia says which member that is with <c>[Content]</c>: <c>Panel.Children</c>,
    /// <c>ContentControl.Content</c>, <c>Decorator.Child</c>, <c>ItemsControl.Items</c>,
    /// <c>TextBlock.Inlines</c>, and whatever a control library marks in its own controls. Asking
    /// the attribute is the only answer that covers the last of those; a list of the framework's
    /// own types answers for the framework and silently fails everyone else's controls.
    /// </para>
    /// <para>
    /// Cached unconditionally, including the absence of one, because unlike an attached property
    /// a content member is a fact about the type that cannot appear later.
    /// </para>
    /// </remarks>
    /// <param name="targetType">The type to ask.</param>
    /// <returns>The member, or <see langword="null"/> when the type declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is <see langword="null"/>.</exception>
    public XamlMemberDescriptor? FindContent(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        return _content.GetOrAdd(targetType, DiscoverContent);
    }

    private XamlMemberDescriptor? DiscoverContent(Type targetType)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        for (Type? declaring = targetType; declaring is not null; declaring = declaring.BaseType)
        {
            foreach (PropertyInfo property in declaring.GetProperties(Declared))
            {
                if (property.GetCustomAttribute<ContentAttribute>(inherit: true) is not null)
                {
                    return Resolve(targetType, property.Name);
                }
            }
        }

        return null;
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

        // An attached property the type does not wrap is a member a document writes by its owner's name —
        // InputElement.IsHoldWithMouseEnabled on a TextBlock — and the XAML compiler resolves the simple
        // name to nothing (ADR 0029).
        if (property.IsAttached && !HasPublicSetter(targetType, property.Name))
        {
            return Resolve(targetType, $"{property.OwnerType.Name}.{property.Name}", property.OwnerType);
        }

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

        if (FindEvent(targetType, name) is { } clrEvent)
        {
            return new XamlMemberDescriptor(
                name, XamlMemberKind.Event, clrEvent.DeclaringType ?? targetType, targetType,
                clrEvent.EventHandlerType ?? typeof(Delegate),
                canRead: false, canWrite: true, isAttached: false, isReadOnly: false)
            {
                Event = clrEvent,
            };
        }

        if (FindProperty(targetType, name) is { } clrProperty)
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

        bool writable = !property.IsReadOnly
            && (name.Contains('.', StringComparison.Ordinal) || HasPublicSetter(targetType, property.Name));

        return new XamlMemberDescriptor(
            name, kind, property.OwnerType, targetType, property.PropertyType,
            canRead: true, canWrite: writable,
            isAttached: property.IsAttached, isReadOnly: !writable)
        {
            AvaloniaProperty = property,
        };
    }

    /// <summary>
    /// Whether a document can write a property by its simple name: through a public CLR setter, the
    /// way Avalonia's XAML compiler — at run time as in the project's build — sets it.
    /// </summary>
    /// <remarks>
    /// The object takes any property that is not read-only, and a document does not. A styled property
    /// whose wrapper only reads — <c>ActualThemeVariant</c> — has no setter the compiler can find, and one
    /// registered with no wrapper at all is a name the compiler does not resolve. Written into a
    /// document, either fails that document's next load and the project's build, so to a document both
    /// are read-only. An attached property written by its owner's name is not asked this; one the registry
    /// answers under its simple name is, because a simple name is what the document would write.
    /// </remarks>
    internal static bool HasPublicSetter(Type targetType, string name) =>
        FindProperty(targetType, name)?.SetMethod is { IsPublic: true };

    /// <summary>Determines whether a name is the type's content property.</summary>
    private static bool IsContentProperty(Type targetType, string name) =>
        FindProperty(targetType, name)?.GetCustomAttribute<ContentAttribute>(inherit: true) is not null;

    private static XamlMemberDescriptor DescribeClr(Type targetType, PropertyInfo property, string name)
    {
        // The content property is where unnamed children go, and a collection-valued member is
        // where they are added rather than assigned. Both change what an edit means, so they
        // are distinguished from an ordinary property.
        bool isContent = property.GetCustomAttribute<ContentAttribute>(inherit: true) is not null;

        // Written through a public setter or not at all: PropertyInfo.CanWrite answers yes for a private one, as
        // StyledElement.IsInitialized has, and the XAML compiler finds no setter there (ADR 0029).
        bool canWrite = property.SetMethod is { IsPublic: true };

        bool isCollection = !canWrite
            && property.PropertyType != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);

        XamlMemberKind kind = isContent
            ? XamlMemberKind.Content
            : isCollection ? XamlMemberKind.Collection : XamlMemberKind.ClrProperty;

        return new XamlMemberDescriptor(
            name, kind, property.DeclaringType ?? targetType, targetType, property.PropertyType,
            canRead: property.CanRead, canWrite: canWrite,
            isAttached: false, isReadOnly: !canWrite)
        {
            ClrProperty = property,
        };
    }

    /// <summary>Finds a property by name, taking the most derived declaration of it.</summary>
    /// <remarks>
    /// <c>Type.GetProperty</c> throws <see cref="AmbiguousMatchException"/> where a type shadows a
    /// base property with <c>new</c> and a different type — <c>Window.PlatformImpl</c> does, and
    /// asking a window about any of its members used to end in that exception rather than an
    /// answer. Walking the hierarchy asks each type about what it declares itself, and the first
    /// answer is the one the document means.
    /// </remarks>
    private static PropertyInfo? FindProperty(Type targetType, string name)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        for (Type? declaring = targetType; declaring is not null; declaring = declaring.BaseType)
        {
            if (declaring.GetProperty(name, Declared) is { } property)
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>Finds an event by name, taking the most derived declaration of it.</summary>
    /// <remarks>Shadowed the same way properties are, and ambiguous in the same way.</remarks>
    private static EventInfo? FindEvent(Type targetType, string name)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        for (Type? declaring = targetType; declaring is not null; declaring = declaring.BaseType)
        {
            if (declaring.GetEvent(name, Declared) is { } declared)
            {
                return declared;
            }
        }

        return null;
    }

    private static XamlMemberDescriptor Unresolved(Type targetType, string name) =>
        new(name, XamlMemberKind.Unknown, targetType, targetType, typeof(object),
            canRead: false, canWrite: false, isAttached: false, isReadOnly: true);
}
