using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The showcase window: one tab per thing the packages do.
/// </summary>
internal sealed class ShowcaseWindow : Window
{
    internal ShowcaseWindow()
    {
        Title = "ArxisStudio.Markup — showcase";
        Width = 1180;
        Height = 780;

        var tabs = new TabControl();

        tabs.Items.Add(new TabItem { Header = "Live", Content = new LiveTab().Content });
        tabs.Items.Add(new TabItem { Header = "Document", Content = DocumentTab.Build() });
        tabs.Items.Add(new TabItem { Header = "Resources", Content = new ResourcesTab().Content });

        var objects = new TabItem { Header = "Objects" };
        var design = new TabItem { Header = "Design mode" };

        tabs.Items.Add(objects);
        tabs.Items.Add(design);

        Content = tabs;

        // Both of these load a document of their own, which cannot happen before the framework
        // has finished starting.
        Dispatcher.UIThread.Post(
            () => _ = FillAsync(objects, design),
            DispatcherPriority.Background);
    }

    private static async Task FillAsync(TabItem objects, TabItem design)
    {
        objects.Content = await ObjectsTab.BuildAsync();
        design.Content = await DesignTab.BuildAsync();
    }
}
