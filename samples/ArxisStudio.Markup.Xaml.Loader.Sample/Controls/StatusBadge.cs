using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// A templated control of the showcase's own — the kind a project writes in code and gives a look
/// in a file of its own.
/// </summary>
/// <remarks>
/// <para>
/// It has no look here. Two documents in <c>Documents/Kinds</c> give it one: a set of styles written
/// the way Avalonia's "Templated Control" template writes it, and a control theme that takes its
/// template from a base theme by <c>BasedOn</c>. Both have <c>Styles</c> or
/// <c>ResourceDictionary</c> at the root, and only the template they set says what they are for —
/// which is the case the classification exists for.
/// </para>
/// <para>
/// Public, because the documents that place it are loaded at run time, and the code Avalonia emits
/// for them can construct only what it is allowed to see.
/// </para>
/// </remarks>
public class StatusBadge : ContentControl
{
}
