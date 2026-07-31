using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A custom control used to prove that a type from an explicitly supplied assembly loads.
/// </summary>
/// <remarks>
/// Deliberately plain. What is being tested is that the assembly was found and the type
/// resolved, not anything the control does.
/// </remarks>
public class CustomBadge : ContentControl
{
    /// <summary>Identifies the <see cref="Caption"/> property.</summary>
    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<CustomBadge, string?>(nameof(Caption));

    /// <summary>Identifies the <see cref="Level"/> property.</summary>
    public static readonly StyledProperty<int> LevelProperty =
        AvaloniaProperty.Register<CustomBadge, int>(nameof(Level), defaultValue: 1);

    /// <summary>Gets or sets the badge's caption.</summary>
    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>Gets or sets the badge's level.</summary>
    public int Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }
}
