using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The one place a loaded document's objects are put on screen.
/// </summary>
/// <remarks>
/// <para>
/// A previewed control belongs to the application it was written for, not to this window, and it
/// has to look like it. Its own surface and its own theme variant are what make that true: a
/// document written against a light theme rendered inside this window's dark one produces grey
/// text on near-white and reads as a rendering fault rather than as the control it is.
/// </para>
/// <para>
/// A backdrop and nothing else. Nothing is drawn over the content, nothing in it is selected,
/// and no input aimed at it is intercepted — it behaves exactly as it would in its own
/// application, because it is that application's control.
/// </para>
/// </remarks>
internal sealed class PreviewHost : Border
{
    private readonly ThemeVariantScope _scope;

    internal PreviewHost()
    {
        _scope = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Light,

            // Top-left, so a control that asks for little space is shown taking little space
            // rather than stretched to fill a pane it never asked for.
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(12);
        MinHeight = 120;
        Child = _scope;
    }

    /// <summary>Gets or sets the loaded root object being shown.</summary>
    internal Control? Preview
    {
        get => _scope.Child;

        // Cleared first: a control belongs to one parent, and handing a second one a control the
        // first still holds is how Avalonia is made to throw.
        set
        {
            _scope.Child = null;
            _scope.Child = value;
        }
    }
}
