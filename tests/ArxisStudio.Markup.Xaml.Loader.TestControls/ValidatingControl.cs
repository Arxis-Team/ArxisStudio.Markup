using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A control with a property that refuses values of the right type.
/// </summary>
/// <remarks>
/// <para>
/// The one thing an update cannot find out before it writes. A member's type says what text
/// converts to it, and an update checks that before touching anything; nothing says what a setter
/// will then accept. A validating property is therefore how a test reaches the case that matters:
/// an update that has already written something and then stops.
/// </para>
/// <para>
/// <c>-1</c> converts to <see cref="int"/> perfectly well, so it passes every check an update can
/// make and fails at the setter — which is exactly the shape of a real validating property.
/// </para>
/// </remarks>
public class ValidatingControl : ContentControl
{
    /// <summary>A property whose value may not be negative.</summary>
    public static readonly StyledProperty<int> LimitProperty =
        AvaloniaProperty.Register<ValidatingControl, int>(
            nameof(Limit), validate: static value => value >= 0);

    /// <summary>Gets or sets a count the control refuses to make negative.</summary>
    public int Limit
    {
        get => GetValue(LimitProperty);
        set => SetValue(LimitProperty, value);
    }
}
