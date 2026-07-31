using System.Reflection;
using BenchmarkDotNet.Running;

namespace ArxisStudio.Markup.Benchmarks;

/// <summary>
/// Benchmark host. Benchmarks are added after the correctness milestones, covering
/// lexing, parsing, unchanged round-trip, single-attribute edits, markup-extension
/// parsing, namespace resolution, type/member resolution and dependency invalidation.
/// </summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
