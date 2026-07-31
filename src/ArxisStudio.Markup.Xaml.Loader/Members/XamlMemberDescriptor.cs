using System;
using System.Reflection;
using Avalonia;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What a member named in the document actually is.
/// </summary>
/// <remarks>
/// Everything a caller needs to decide whether an edit is possible, in one place: what kind of
/// member it is, what type it holds, whether it can be written, and the underlying metadata for
/// callers that need to go further. Working any of that out from the raw reflection surface is
/// easy to get subtly wrong — a read-only direct property looks writable until it throws.
/// </remarks>
public sealed class XamlMemberDescriptor
{
    internal XamlMemberDescriptor(
        string name,
        XamlMemberKind kind,
        Type declaringType,
        Type targetType,
        Type valueType,
        bool canRead,
        bool canWrite,
        bool isAttached,
        bool isReadOnly)
    {
        Name = name;
        Kind = kind;
        DeclaringType = declaringType;
        TargetType = targetType;
        ValueType = valueType;
        CanRead = canRead;
        CanWrite = canWrite;
        IsAttached = isAttached;
        IsReadOnly = isReadOnly;
    }

    /// <summary>Gets the member's name as the document wrote it.</summary>
    public string Name { get; }

    /// <summary>Gets what kind of member it is.</summary>
    public XamlMemberKind Kind { get; }

    /// <summary>Gets the type that declares the member, which for an attached member is its owner.</summary>
    public Type DeclaringType { get; }

    /// <summary>Gets the type the member was resolved against.</summary>
    public Type TargetType { get; }

    /// <summary>Gets the type of value the member holds, or the handler type for an event.</summary>
    public Type ValueType { get; }

    /// <summary>Gets a value indicating whether the member's value can be read.</summary>
    public bool CanRead { get; }

    /// <summary>Gets a value indicating whether the member's value can be written.</summary>
    public bool CanWrite { get; }

    /// <summary>Gets a value indicating whether the member is attached rather than declared on the target.</summary>
    public bool IsAttached { get; }

    /// <summary>Gets a value indicating whether the member is read-only.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets the Avalonia property behind the member, when there is one.</summary>
    public AvaloniaProperty? AvaloniaProperty { get; init; }

    /// <summary>Gets the CLR property behind the member, when there is one.</summary>
    public PropertyInfo? ClrProperty { get; init; }

    /// <summary>Gets the event behind the member, when it is one.</summary>
    public EventInfo? Event { get; init; }

    /// <summary>Gets the attached accessor pair behind the member, when it has one.</summary>
    public (MethodInfo? Getter, MethodInfo? Setter) AttachedAccessors { get; init; }

    /// <summary>Gets a value indicating whether the member resolved to anything at all.</summary>
    public bool IsResolved => Kind != XamlMemberKind.Unknown;

    /// <summary>Returns the member's kind, name and value type.</summary>
    /// <returns>A readable description of the member.</returns>
    public override string ToString() => $"{Kind} {DeclaringType.Name}.{Name} : {ValueType.Name}";
}
