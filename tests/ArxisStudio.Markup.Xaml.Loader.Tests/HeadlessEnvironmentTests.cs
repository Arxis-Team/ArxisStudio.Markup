using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// Milestone 0 proves the headless Avalonia stack this project's later tests depend on
/// actually initialises. Loading, resolution, mapping and synchronization tests arrive
/// with milestones 6 to 10.
/// </summary>
public sealed class HeadlessEnvironmentTests
{
    [Fact]
    public void LoaderPackageAssembly_Loads()
    {
        Assembly assembly = Assembly.Load(new AssemblyName("ArxisStudio.Markup.Xaml.Loader"));

        Assert.Equal("ArxisStudio.Markup.Xaml.Loader", assembly.GetName().Name);
    }

    [AvaloniaFact]
    public void HeadlessApplication_IsInitialisedOnTheAvaloniaThread()
    {
        Assert.NotNull(Application.Current);
        Assert.True(Dispatcher.UIThread.CheckAccess());
    }

    [AvaloniaFact]
    public void RealAvaloniaControls_CanBeCreated()
    {
        var button = new Button { Width = 320d };

        Assert.Equal(320d, button.Width);
    }
}
