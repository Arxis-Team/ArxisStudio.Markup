using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A window of a library's own, for a document whose root is a type derived from
/// <see cref="Window"/> rather than <see cref="Window"/> itself.
/// </summary>
/// <remarks>
/// Empty on purpose: what is being tested is that a document rooted in it is still a window, which
/// only its base type can say.
/// </remarks>
public class ToolWindow : Window
{
}
