using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A form the way a project writes one: its constructor loads its own markup and then reads a
/// named control out of it.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape the double-initialisation hazard has. The compiled markup declares a keyed
/// resource and a style on the root and a named control inside it, so a session that constructs
/// the type and then populates it a second time shows every symptom at once: the resource is
/// added under a key the dictionary already holds, the style is there twice, and the control the
/// constructor kept is one the second population threw away.
/// </para>
/// <para>
/// The constructor does by hand what a generated <c>InitializeComponent</c> does — the load call
/// Avalonia's XAML compiler rewrites into the populate trampoline, then the lookup in the name
/// scope that populate filled — so nothing here depends on a source generator being present.
/// </para>
/// </remarks>
public partial class SelfLoadingView : UserControl
{
    /// <summary>Creates the view, populates it, and keeps the control its markup names.</summary>
    public SelfLoadingView()
    {
        AvaloniaXamlLoader.Load(this);

        TitleSeenByTheConstructor = this.FindNameScope()?.Find<TextBlock>("Title");
    }

    /// <summary>Gets the control named <c>Title</c> as the constructor found it.</summary>
    public TextBlock? TitleSeenByTheConstructor { get; }
}
