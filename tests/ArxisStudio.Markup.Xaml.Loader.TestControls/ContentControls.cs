using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A control that keeps its children somewhere of its own, said with <c>[Content]</c>.
/// </summary>
/// <remarks>
/// Not a <see cref="Panel"/> and not an <see cref="ItemsControl"/>: the point is a control library
/// that declares its own content member, which is what every real one does and what a list of the
/// framework's own types cannot be made to cover.
/// </remarks>
public class SlotHost : Control
{
    /// <summary>Gets the controls the host arranges.</summary>
    [Content]
    public ObservableCollection<Control> Slots { get; } = [];
}

/// <summary>
/// A control whose content is a single object of its own naming.
/// </summary>
public class ShellHost : Control
{
    /// <summary>Identifies the <see cref="Body"/> property.</summary>
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<ShellHost, object?>(nameof(Body));

    /// <summary>Gets or sets what the shell shows.</summary>
    [Content]
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
