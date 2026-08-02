namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// What duplicating an element does with the names inside it.
/// </summary>
/// <remarks>
/// A name identifies one element within a scope, so a copy that keeps the original's names has
/// declared each of them twice — and a loader that enforces the rule, as Avalonia's does, refuses
/// the document outright. There is no answer that suits every tool, so this is the caller's to
/// pick.
/// </remarks>
public enum XamlDuplicateNames
{
    /// <summary>
    /// Take every <c>x:Name</c> and <c>Name</c> out of the copy, leaving it anonymous.
    /// </summary>
    Remove,

    /// <summary>
    /// Copy the names as they are, for a caller that is about to rename them itself.
    /// </summary>
    Keep,
}
