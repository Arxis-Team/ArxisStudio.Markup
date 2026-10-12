using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A second compiled control, so the tests can place one inside the other's live document.
/// </summary>
/// <remarks>
/// Its compiled markup deliberately places nothing, so that a cycle between the two is one of live
/// documents only. A project whose compiled markup states a cycle builds as well, and constructs
/// without end at run time — <see cref="SelfPlacingView"/> is that case.
/// </remarks>
public partial class LiveHostControl : UserControl
{
    /// <summary>Creates the control and populates it, the way a generated partial would.</summary>
    public LiveHostControl() => AvaloniaXamlLoader.Load(this);
}
