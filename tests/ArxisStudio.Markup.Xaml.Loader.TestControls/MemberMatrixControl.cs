using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A control carrying one member of every kind the property matrix has to cover.
/// </summary>
/// <remarks>
/// Written for the tests rather than found among Avalonia's own controls, so that each kind is
/// present, unambiguous and stable. A matrix built from whichever real controls happen to have
/// the right shapes would silently lose a case the day one of them changed.
/// </remarks>
public class MemberMatrixControl : ContentControl
{
    /// <summary>A styled property.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<MemberMatrixControl, string?>(nameof(Label));

    /// <summary>A direct property, readable and writable.</summary>
    public static readonly DirectProperty<MemberMatrixControl, int> CounterProperty =
        AvaloniaProperty.RegisterDirect<MemberMatrixControl, int>(
            nameof(Counter), o => o.Counter, (o, v) => o.Counter = v);

    /// <summary>A direct property with no setter, which cannot be written.</summary>
    public static readonly DirectProperty<MemberMatrixControl, string> ComputedProperty =
        AvaloniaProperty.RegisterDirect<MemberMatrixControl, string>(
            nameof(Computed), o => o.Computed);

    /// <summary>An attached property, registered against any styled element.</summary>
    public static readonly AttachedProperty<int> SlotProperty =
        AvaloniaProperty.RegisterAttached<MemberMatrixControl, StyledElement, int>("Slot");

    private readonly ObservableCollection<string> _items = [];

    private int _counter;

    /// <summary>Gets or sets the styled property's value.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the direct property's value.</summary>
    public int Counter
    {
        get => _counter;
        set => SetAndRaise(CounterProperty, ref _counter, value);
    }

    /// <summary>Gets a value that is derived rather than stored.</summary>
    public string Computed => $"label={Label}, counter={Counter}";

    /// <summary>Gets or sets an ordinary CLR property, with no Avalonia property behind it.</summary>
    public string? Note { get; set; }

    /// <summary>Gets a collection members are added to rather than assigned.</summary>
    public IList<string> Items => _items;

    /// <summary>Reads the attached property from an element.</summary>
    /// <param name="element">The element to read from.</param>
    /// <returns>The element's slot.</returns>
    public static int GetSlot(StyledElement element) => element.GetValue(SlotProperty);

    /// <summary>Writes the attached property onto an element.</summary>
    /// <param name="element">The element to write to.</param>
    /// <param name="value">The slot to set.</param>
    public static void SetSlot(StyledElement element, int value) => element.SetValue(SlotProperty, value);
}
