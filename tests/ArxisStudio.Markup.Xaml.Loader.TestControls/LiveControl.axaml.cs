using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A control whose markup is compiled into this assembly, the way a user project's controls are.
/// </summary>
/// <remarks>
/// The load call in the constructor is what Avalonia's XAML compiler rewrites into the generated
/// populate trampoline — the seam live population rides on. The tests that register documents for
/// this type are testing exactly what a designer does to a project's own placed controls.
/// </remarks>
public partial class LiveControl : UserControl
{
    /// <summary>Creates the control and populates it, the way a generated partial would.</summary>
    public LiveControl() => AvaloniaXamlLoader.Load(this);
}
