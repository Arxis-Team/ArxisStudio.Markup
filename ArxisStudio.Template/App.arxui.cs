using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ArxisStudio.Markup.Template.Views;

namespace ArxisStudio.Markup.Template;

public partial class App : Application
{
    public override void Initialize()
    {
        InitializeComponent();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
