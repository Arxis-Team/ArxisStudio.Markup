using System.Reflection;
using Xunit;

namespace ArxisStudio.Markup.Tests;

/// <summary>
/// Milestone 0 only proves the project runs. The suite listed in the contract's
/// testing strategy — spans, line mapping, snapshots, versioning, provider precedence,
/// transactions, rollback, undo/redo, dependency graph, concurrent reads — arrives
/// with milestones 1 and 2.
/// </summary>
public sealed class PackageSmokeTests
{
    [Fact]
    public void BasePackageAssembly_Loads()
    {
        Assembly assembly = Assembly.Load(new AssemblyName("ArxisStudio.Markup"));

        Assert.Equal("ArxisStudio.Markup", assembly.GetName().Name);
    }
}
