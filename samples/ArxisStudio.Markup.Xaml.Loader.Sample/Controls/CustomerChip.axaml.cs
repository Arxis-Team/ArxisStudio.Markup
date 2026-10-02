using Avalonia.Controls;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Controls;

/// <summary>
/// A control whose markup is compiled into the showcase, the way a project's own controls are.
/// </summary>
/// <remarks>
/// <para>
/// What it shows comes from <c>CustomerChip.axaml</c> as it was when the showcase was built:
/// Avalonia's XAML compiler turned that file into the code <see cref="InitializeComponent"/> runs.
/// Placed in another document, it is constructed by Avalonia and shows exactly that, however the
/// file has been edited since — unless a document is registered for it with
/// <see cref="XamlLivePopulation"/>, which is what the section "Вложенные контролы" does.
/// </para>
/// <para>
/// Public, because the document that places it is loaded at run time.
/// </para>
/// </remarks>
public partial class CustomerChip : UserControl
{
    /// <summary>Creates the chip and populates it.</summary>
    public CustomerChip() => InitializeComponent();
}
