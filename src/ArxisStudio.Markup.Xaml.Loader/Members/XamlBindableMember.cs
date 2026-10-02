namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>A member a binding can name on its source, said in names only.</summary>
/// <remarks>
/// What a data panel lists for a source type. Names, so that a list on screen keeps nothing of the
/// assembly it was read from; a member's own type, when a tool wants to go a step further down the
/// path, is <see cref="XamlMemberResolver.ResolveBindingPath"/>'s to answer.
/// </remarks>
/// <param name="Name">The member's name, as a binding path writes it.</param>
/// <param name="TypeName">The member's type as a reader writes it — <c>string</c>, <c>int?</c>, <c>ObservableCollection&lt;Customer&gt;</c>.</param>
/// <param name="CanWrite">Whether a two-way binding can write to it.</param>
/// <param name="IsCollection">Whether it holds a collection, which is what an items control's source binds to.</param>
/// <param name="IsCommand">Whether it holds a command, which is what a button's command binds to.</param>
public sealed record XamlBindableMember(
    string Name,
    string TypeName,
    bool CanWrite,
    bool IsCollection,
    bool IsCommand);
