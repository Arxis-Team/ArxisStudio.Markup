using Avalonia.Controls;
using Avalonia.Media;

namespace AvaloniaDesigner.Generator.Sample.Forms;

public partial class MyUserControl
{
    public MyUserControl()
    {
        InitializeComponent();
        Viewbox dockPanel = new Viewbox();
        dockPanel.Stretch = Stretch.Uniform;

    }
}