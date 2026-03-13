using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Json.Generator.Tests
{
    public class SolidColorBrushTests
    {
        private const string DummyUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Forms
{
    public partial class SolidColorBrush : UserControl
    {
    }
}
";

        private const string JsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""UserControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.Border"",
        ""Properties"": {
          ""Name"": ""NamedBorder"",
          ""BorderThickness"": ""3"",
          ""BorderBrush"": ""Gray"",

          ""Background"": {
            ""TypeName"": ""Avalonia.Media.SolidColorBrush"",
            ""Properties"": {
              ""Color"": ""LightGreen"",
              ""Opacity"": 0.5
            }
          },

          ""Child"": {
            ""TypeName"": ""Avalonia.Controls.TextBlock"",
            ""Properties"": {
              ""Text"": ""Hello Brush!""
            }
          }
        }
      }
    }
  }
}
";

        [Fact]
        public void Opacity_should_be_generated_as_literal_not_double_parse()
        {
            var solidColorBrushSource = GeneratorTestHelper.GetGeneratedSource(
                DummyUserControlSource,
                "SolidColorBrush.arxui.cs",
                "TestApp.Forms.SolidColorBrush.g.cs",
                ("SolidColorBrush.arxui", JsonModel));

            Assert.DoesNotContain("double.Parse(\"0.5\")", solidColorBrushSource);
            Assert.Contains("Opacity = 0.5", solidColorBrushSource);
        }
    }
}
