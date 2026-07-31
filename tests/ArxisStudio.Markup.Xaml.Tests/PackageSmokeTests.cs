using System.Reflection;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Tests;

/// <summary>
/// Milestone 0 only proves the project runs. Golden round-trip fixtures, malformed-input
/// recovery, markup-extension parsing and editing tests arrive with milestones 3 to 5.
/// </summary>
public sealed class PackageSmokeTests
{
    [Fact]
    public void SyntaxPackageAssembly_Loads()
    {
        Assembly assembly = Assembly.Load(new AssemblyName("ArxisStudio.Markup.Xaml"));

        Assert.Equal("ArxisStudio.Markup.Xaml", assembly.GetName().Name);
    }
}
