using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Views;

/// <summary>
/// The showcase window: a rail of sections, and one view per thing the packages do.
/// </summary>
/// <remarks>
/// The window's whole interface is markup. The rail is a <see cref="TabControl" /> wearing the
/// control theme in <c>Themes/Showcase.axaml</c>, and each section is a view of its own, built the
/// first time it is shown.
/// </remarks>
internal sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
