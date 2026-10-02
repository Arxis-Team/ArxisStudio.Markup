namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>A type a document can name, said in names only.</summary>
/// <remarks>
/// Names rather than the type, because what a tool builds from a catalog outlives the assemblies the
/// catalog was read from. A toolbox filled from one generation of a project's code is still on screen
/// while the next generation loads, and a <see cref="System.Type"/> in it would keep the previous
/// generation in memory for as long as the toolbox lives.
/// </remarks>
/// <param name="FullName">The CLR name with its namespace — what <c>x:Class</c> names and a resolver finds.</param>
/// <param name="Name">The name an element of the type is written with.</param>
/// <param name="ClrNamespace">The CLR namespace.</param>
/// <param name="AssemblyName">The simple name of the assembly that defines the type.</param>
/// <param name="XmlNamespace">
/// The namespace a document writes the type in: the one its assembly maps the CLR namespace to, or
/// <c>using:</c> and the CLR namespace where the assembly maps none.
/// </param>
/// <param name="SuggestedPrefix">
/// The prefix the assembly suggests for <paramref name="XmlNamespace"/>, or <see langword="null"/>.
/// </param>
/// <param name="Kinds">What a document can do with the type.</param>
public sealed record XamlTypeEntry(
    string FullName,
    string Name,
    string ClrNamespace,
    string AssemblyName,
    string XmlNamespace,
    string? SuggestedPrefix,
    XamlTypeKinds Kinds);
