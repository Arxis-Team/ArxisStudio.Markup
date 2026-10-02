using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// A control of the showcase's own that is not templated: a border with the accent it was made
/// for, written entirely in code.
/// </summary>
/// <remarks>
/// A document may have it at the root. That makes the document a control — the classifier's
/// <c>Control</c> — and one written outside Avalonia's namespace, which is what tells it apart from
/// a document rooted in Avalonia's own <c>Border</c>.
/// </remarks>
public class AccentTile : Border
{
    /// <summary>Creates a tile wearing the accent.</summary>
    public AccentTile()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xED, 0xFC));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x63, 0xCC));
        BorderThickness = new Thickness(0, 0, 0, 3);
        CornerRadius = new CornerRadius(8);
    }
}
