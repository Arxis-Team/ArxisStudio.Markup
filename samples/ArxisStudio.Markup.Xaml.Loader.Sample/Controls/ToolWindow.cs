using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// A base window of the showcase's own, for a document whose root is a window written in a
/// namespace of the project rather than Avalonia's <c>Window</c> itself.
/// </summary>
/// <remarks>
/// Empty on purpose. That a document rooted in it is a window is something only its base type can
/// say — its name could be anything — and saying it is the classifier's job.
/// </remarks>
public class ToolWindow : Window
{
}
