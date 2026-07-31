using System;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// The documents the showcase works on, written the way real ones are: awkward indentation,
/// comments in the middle, a mix of literals, bindings and resource references.
/// </summary>
internal static class Fixtures
{
    internal const string AvaloniaNamespace = "https://github.com/avaloniaui";
    internal const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    internal const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    internal const string CompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    internal static Uri ViewUri { get; } = new("file:///Views/CustomerView.axaml");

    internal static Uri PaletteUri { get; } = new("file:///Themes/Palette.axaml");

    internal static Uri BrandUri { get; } = new("file:///Themes/Brand.axaml");

    /// <summary>A view with the mix of things a real one has, including things nothing here knows.</summary>
    internal static string View { get; } =
        $$"""
        <UserControl xmlns="{{AvaloniaNamespace}}"
                     xmlns:x="{{XamlNamespace}}"
                     xmlns:d="{{DesignNamespace}}"
                     xmlns:mc="{{CompatibilityNamespace}}"
                     mc:Ignorable="d"
                     d:DesignWidth="480" d:DesignHeight="240">

          <!-- The resources this view uses come from a file it does not own. -->
          <UserControl.Resources>
            <ResourceDictionary>
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="/Themes/Palette.axaml" />
              </ResourceDictionary.MergedDictionaries>
              <Thickness x:Key="RowPadding">8,4</Thickness>
            </ResourceDictionary>
          </UserControl.Resources>

          <StackPanel Orientation="Vertical"     Spacing="6">
            <TextBlock Name="Title"
                       Text="{Binding Customer.Name}"
                       d:Text="Ada Lovelace"
                       Foreground="{DynamicResource Accent}" />
            <Border Background="{StaticResource Surface}"
                    Padding="{StaticResource RowPadding}">
              <Button Name="Save" Content="Save" Width="120" />
            </Border>
          </StackPanel>
        </UserControl>
        """;

    /// <summary>The palette the view includes, itself merging a third file.</summary>
    internal static string Palette(string accent) =>
        $$"""
        <ResourceDictionary xmlns="{{AvaloniaNamespace}}" xmlns:x="{{XamlNamespace}}">
          <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="Brand.axaml" />
          </ResourceDictionary.MergedDictionaries>
          <SolidColorBrush x:Key="Accent" Color="{{accent}}" />
        </ResourceDictionary>
        """;

    /// <summary>The file at the bottom of the chain.</summary>
    internal static string Brand { get; } =
        $$"""
        <ResourceDictionary xmlns="{{AvaloniaNamespace}}" xmlns:x="{{XamlNamespace}}">
          <SolidColorBrush x:Key="Surface" Color="#FFF2F2F2" />
        </ResourceDictionary>
        """;

    /// <summary>A document that does not parse, to show what survives one.</summary>
    internal static string Malformed { get; } =
        $$"""
        <UserControl xmlns="{{AvaloniaNamespace}}">
          <StackPanel>
            <!-- an end tag that closes the wrong element, and an attribute with no value -->
            <TextBlock Text="half
            <Button Content
          </Grid>
        </UserControl>
        """;
}
