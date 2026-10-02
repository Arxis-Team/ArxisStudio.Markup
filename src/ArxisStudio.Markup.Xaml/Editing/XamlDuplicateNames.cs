namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// What copying an element — duplicating it, or inserting a fragment of another document — does
/// with the names inside it.
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

    /// <summary>
    /// Take out only the names the document receiving the copy already declares, and keep the rest.
    /// </summary>
    /// <remarks>
    /// What moving markup between documents wants: a name nothing in the target uses is still the
    /// name the code behind the form refers to. A duplicate within one document collides on every
    /// name — the original is still there — so for <c>DuplicateElement</c> this is
    /// <see cref="Remove"/>. Names inside a template count against the whole document, which
    /// errs towards taking one out that could have stayed.
    /// </remarks>
    RemoveConflicting,
}
