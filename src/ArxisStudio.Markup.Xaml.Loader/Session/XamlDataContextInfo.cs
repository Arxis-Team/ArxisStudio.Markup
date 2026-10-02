using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What the bindings written on an element read from: the data type they are written against, and the
/// design data a designer shows them with.
/// </summary>
/// <remarks>
/// <para>
/// Two different answers, and a data panel needs both. <see cref="DataType"/> is what the document
/// says — the nearest <c>x:DataType</c> — and is what compiled bindings are checked against and what a
/// panel offers members of. <see cref="DesignDataContextType"/> is what the design view shows the
/// bindings with — <c>Design.DataContext</c>, or whatever the class sets in its constructor — and is
/// what a binding written without a data type is read against.
/// </para>
/// <para>
/// An answer to one question at one moment: the types are of whatever generation of code the session
/// was built in, and a tool that keeps them keeps that generation. Read what is needed and let them go.
/// </para>
/// </remarks>
public sealed class XamlDataContextInfo
{
    /// <summary>Gets the element whose <c>x:DataType</c> is in scope, or <see langword="null"/> when none is.</summary>
    public XamlElement? DataTypeElement { get; init; }

    /// <summary>Gets the <c>x:DataType</c> in scope as it is written, or <see langword="null"/>.</summary>
    public string? WrittenDataType { get; init; }

    /// <summary>
    /// Gets the type the <c>x:DataType</c> in scope names, or <see langword="null"/> when none is in
    /// scope or it names nothing the environment resolves.
    /// </summary>
    public Type? DataType { get; init; }

    /// <summary>
    /// Gets the type of the data the element's object shows its bindings with, or <see langword="null"/>
    /// when it has none — or no object, or nothing to bind with at all.
    /// </summary>
    public Type? DesignDataContextType { get; init; }

    /// <summary>
    /// Gets a value indicating whether a binding written on the element is compiled — the nearest
    /// <c>x:CompileBindings</c>, or the session's default where none is written.
    /// </summary>
    public required bool CompilesBindings { get; init; }
}
