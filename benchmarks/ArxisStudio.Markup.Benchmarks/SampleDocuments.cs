using System;
using System.Text;

namespace ArxisStudio.Markup.Benchmarks;

/// <summary>
/// The documents the benchmarks run against.
/// </summary>
/// <remarks>
/// Generated rather than checked in so that the size is a number in one place and the shape is
/// obviously representative: a view of nested panels with the mix of literals, bindings, static
/// resources and comments a real one has. A tiny document measures the cost of starting, which
/// is not the cost anything is optimised for.
/// </remarks>
internal static class SampleDocuments
{
    internal const string AvaloniaNamespace = "https://github.com/avaloniaui";
    internal const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Builds a view with the given number of leaf controls.</summary>
    /// <param name="controls">How many leaf controls to write.</param>
    /// <returns>The document's text.</returns>
    internal static string View(int controls)
    {
        var text = new StringBuilder();

        text.Append($"<UserControl xmlns=\"{AvaloniaNamespace}\"\n")
            .Append($"             xmlns:x=\"{XamlNamespace}\">\n")
            .Append("  <UserControl.Resources>\n")
            .Append("    <SolidColorBrush x:Key=\"Accent\" Color=\"#FF3366CC\" />\n")
            .Append("    <SolidColorBrush x:Key=\"Muted\" Color=\"#FF888888\" />\n")
            .Append("  </UserControl.Resources>\n")
            .Append("  <StackPanel Orientation=\"Vertical\" Spacing=\"8\">\n");

        for (int index = 0; index < controls; index++)
        {
            text.Append("    <!-- row ").Append(index).Append(" -->\n")
                .Append("    <Border Background=\"{StaticResource Accent}\" Padding=\"4,2\">\n")
                .Append("      <StackPanel Orientation=\"Horizontal\">\n")
                .Append("        <TextBlock Text=\"{Binding Rows[").Append(index).Append("].Name}\"\n")
                .Append("                   Foreground=\"{StaticResource Muted}\" Width=\"120\" />\n")
                .Append("        <Button Content=\"Row ").Append(index).Append("\" IsEnabled=\"True\" />\n")
                .Append("      </StackPanel>\n")
                .Append("    </Border>\n");
        }

        return text.Append("  </StackPanel>\n").Append("</UserControl>\n").ToString();
    }

    /// <summary>Builds a resource dictionary that merges another one.</summary>
    /// <param name="include">The source to merge, or <see langword="null"/> for a leaf dictionary.</param>
    /// <param name="key">The key the dictionary defines.</param>
    /// <returns>The document's text.</returns>
    internal static string Dictionary(string? include, string key) =>
        $"<ResourceDictionary xmlns=\"{AvaloniaNamespace}\" xmlns:x=\"{XamlNamespace}\">\n" +
        (include is null
            ? string.Empty
            : "  <ResourceDictionary.MergedDictionaries>\n" +
              $"    <ResourceInclude Source=\"{include}\" />\n" +
              "  </ResourceDictionary.MergedDictionaries>\n") +
        $"  <SolidColorBrush x:Key=\"{key}\" Color=\"Red\" />\n" +
        "</ResourceDictionary>\n";
}
