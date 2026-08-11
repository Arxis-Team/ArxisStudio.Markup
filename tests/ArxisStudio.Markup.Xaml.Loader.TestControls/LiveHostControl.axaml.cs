using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A second compiled control, so the tests can place one inside the other's live document.
/// </summary>
/// <remarks>
/// Its compiled markup deliberately places nothing, so that a cycle between the two can only be
/// created by live documents — which is the one way a cycle can arise, since a project with a
/// compiled cycle would never have built.
/// </remarks>
public partial class LiveHostControl : UserControl
{
    /// <summary>Creates the control and populates it, the way a generated partial would.</summary>
    public LiveHostControl() => AvaloniaXamlLoader.Load(this);
}
