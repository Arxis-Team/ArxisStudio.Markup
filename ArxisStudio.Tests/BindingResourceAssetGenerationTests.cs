using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    /// <summary>
    /// Тесты генерации кода для binding/resource/asset-сценариев.
    /// </summary>
    public class BindingResourceAssetGenerationTests
    {
        private const string DummyUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Views
{
    public partial class BindingResourceAssetControl : UserControl
    {
    }
}
";

        private const string JsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.BindingResourceAssetControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Styles"": [
      {
        ""$styleInclude"": ""avares://Sample.Assembly/Styles/BindingResourceAsset.axaml""
      },
      {
        ""TypeName"": ""Avalonia.Styling.Style"",
        ""Properties"": {}
      }
    ],
    ""Resources"": {
      ""$mergedDictionaries"": [
        {
          ""Source"": ""avares://Sample.Assembly/Resources/Common.axaml""
        }
      ],
      ""LocalBorder"": {
        ""TypeName"": ""Avalonia.Controls.Border"",
        ""Properties"": {
          ""Name"": ""LocalBorderResource"",
          ""Width"": 100
        }
      }
    },
    ""Properties"": {
      ""Background"": {
        ""$resource"": ""BackgroundBrush""
      },
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.StackPanel"",
        ""Properties"": {
          ""Children"": [
            {
              ""TypeName"": ""Avalonia.Controls.TextBlock"",
              ""Properties"": {
                ""Name"": ""MessageText"",
                ""Foreground"": {
                  ""$resource"": ""ForegroundBrush""
                },
                ""Text"": {
                  ""$binding"": ""UserName"",
                  ""Mode"": ""TwoWay"",
                  ""StringFormat"": ""Hello {0}"",
                  ""RelativeSource"": {
                    ""Mode"": ""Self""
                  }
                }
              }
            },
            {
              ""TypeName"": ""Avalonia.Controls.Image"",
              ""Properties"": {
                ""Name"": ""AvatarImage"",
                ""Source"": {
                  ""$asset"": {
                    ""Path"": ""/Assets/avatar.png"",
                    ""Assembly"": ""Sample.Assembly""
                  }
                }
              }
            }
          ]
        }
      }
    }
  }
}
";

        /// <summary>
        /// Проверяет генерацию binding, resource и asset-значений.
        /// </summary>
        [Fact]
        public void Binding_resource_and_asset_values_should_be_generated_correctly()
        {
            var source = GeneratorTestHelper.GetGeneratedSource(
                DummyUserControlSource,
                "BindingResourceAssetControl.arxui.cs",
                "TestApp.Views.BindingResourceAssetControl.g.cs",
                ("BindingResourceAssetControl.arxui", JsonModel));

            Assert.Contains(
                "this.Bind(global::Avalonia.Controls.UserControl.BackgroundProperty, this.GetResourceObservable(\"BackgroundBrush\"));",
                source);
            Assert.Contains(
                "this.Resources.MergedDictionaries.Add(new global::Avalonia.Markup.Xaml.Styling.ResourceInclude(new global::System.Uri(\"avares://Sample.Assembly/Resources/Common.axaml\")) { Source = new global::System.Uri(\"avares://Sample.Assembly/Resources/Common.axaml\") });",
                source);
            Assert.Contains(
                "this.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new global::System.Uri(\"avares://Sample.Assembly/Styles/BindingResourceAsset.axaml\")) { Source = new global::System.Uri(\"avares://Sample.Assembly/Styles/BindingResourceAsset.axaml\") });",
                source);
            Assert.Contains(
                "this.Resources[\"LocalBorder\"] = this.LocalBorderResource;",
                source);
            Assert.Contains(
                "this.MessageText.Bind(global::Avalonia.Controls.TextBlock.TextProperty, new global::Avalonia.Data.Binding(\"UserName\") { Mode = global::Avalonia.Data.BindingMode.TwoWay, StringFormat = \"Hello {0}\", RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.Self) });",
                source);
            Assert.Contains(
                "this.MessageText.Bind(global::Avalonia.Controls.TextBlock.ForegroundProperty, this.GetResourceObservable(\"ForegroundBrush\"));",
                source);
            Assert.Contains(
                "this.AvatarImage.Source = new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open(new global::System.Uri(\"avares://Sample.Assembly/Assets/avatar.png\")));",
                source);
        }

        /// <summary>
        /// Проверяет экранирование многострочных строковых литералов.
        /// </summary>
        [Fact]
        public void Multiline_string_values_should_be_escaped_in_generated_code()
        {
            const string multilineJson = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.BindingResourceAssetControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.TextBlock"",
        ""Properties"": {
          ""Text"": ""Первая строка\nВторая строка\tс табом""
        }
      }
    }
  }
}
";

            var source = GeneratorTestHelper.GetGeneratedSource(
                DummyUserControlSource,
                "BindingResourceAssetControl.arxui.cs",
                "TestApp.Views.BindingResourceAssetControl.g.cs",
                ("BindingResourceAssetControl.arxui", multilineJson));

            Assert.Contains("\\n", source);
            Assert.Contains("\\t", source);
            Assert.Contains("Первая строка\\nВторая строка\\tс табом", source);
        }
    }
}
