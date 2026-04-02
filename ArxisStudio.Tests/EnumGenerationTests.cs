using System.Linq;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    /// <summary>
    /// Тесты генерации enum-значений в выходном коде.
    /// </summary>
    public class EnumGenerationTests
    {
        // Небольшой заглушечный исходник с partial UserControl под генерацию
        private const string DummyUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Forms
{
    public partial class EnumTestControl : UserControl
    {
    }
}
";

        private const string EnumJsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Forms.EnumTestControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.Border"",
        ""Properties"": {
          ""Child"": {
            ""TypeName"": ""Avalonia.Controls.TextBlock"",
            ""Properties"": {
              ""Name"": ""EnumText"",
              ""Text"": ""Hello enums!"",
              ""HorizontalAlignment"": ""Center"",
              ""VerticalAlignment"": ""Bottom"",
              ""TextWrapping"": ""Wrap"",
              ""FontWeight"": ""Bold""
            }
          }
        }
      }
    }
  }
}
";

        private const string DockPanelJsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Forms.DockPanelControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.DockPanel"",
        ""Properties"": {
          ""Name"": ""RootDock"",
          ""Children"": [
            {
              ""TypeName"": ""Avalonia.Controls.Border"",
              ""Properties"": {
                ""Name"": ""TopBorder"",
                ""Avalonia.Controls.DockPanel.Dock"": ""Top""
              }
            }
          ]
        }
      }
    }
  }
}
";

        private const string AvaloniaDesignAttachedPropertiesJsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Forms.EnumTestControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Avalonia.Controls.Design.Width"": 800,
      ""Avalonia.Controls.Design.Height"": 600
    }
  }
}
";

        /// <summary>
        /// Проверяет генерацию fully-qualified enum-значений для обычных свойств.
        /// </summary>
        [Fact]
        public void Enum_properties_should_be_generated_with_fully_qualified_enum_members()
        {
            var source = GeneratorTestHelper.GetGeneratedSource(
                DummyUserControlSource,
                "EnumTestControl.arxui.cs",
                "TestApp.Forms.EnumTestControl.g.cs",
                ("EnumTestControl.arxui", EnumJsonModel));

            Assert.Contains("internal global::Avalonia.Controls.TextBlock EnumText;", source);
            Assert.Contains(
                "this.EnumText.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;",
                source);
            Assert.Contains(
                "this.EnumText.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom;",
                source);
            Assert.Contains(
                "this.EnumText.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;",
                source);
            Assert.Contains(
                "this.EnumText.FontWeight = global::Avalonia.Media.FontWeight.Bold;",
                source);
        }

        /// <summary>
        /// Проверяет генерацию attached enum-свойства <c>DockPanel.Dock</c>.
        /// </summary>
        [Fact]
        public void Attached_enum_DockPanel_Dock_should_be_generated_correctly()
        {
            const string dockPanelUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Forms
{
    public partial class DockPanelControl : UserControl
    {
    }
}
";

            var source = GeneratorTestHelper.GetGeneratedSource(
                dockPanelUserControlSource,
                "DockPanelControl.arxui.cs",
                "TestApp.Forms.DockPanelControl.g.cs",
                ("DockPanelControl.arxui", DockPanelJsonModel));
            
            Assert.Contains("internal global::Avalonia.Controls.Border TopBorder;", source);
            Assert.Contains(
                "global::Avalonia.Controls.DockPanel.SetDock(this.TopBorder, global::Avalonia.Controls.Dock.Top);",
                source);
        }

        /// <summary>
        /// Проверяет, что attached-свойства <c>Avalonia.Controls.Design.*</c> генерируются без условной компиляции.
        /// </summary>
        [Fact]
        public void Avalonia_design_attached_properties_should_be_generated_without_debug_preprocessor_blocks()
        {
            var source = GeneratorTestHelper.GetGeneratedSource(
                DummyUserControlSource,
                "EnumTestControl.arxui.cs",
                "TestApp.Forms.EnumTestControl.g.cs",
                ("EnumTestControl.arxui", AvaloniaDesignAttachedPropertiesJsonModel));

            Assert.Contains("global::Avalonia.Controls.Design.SetWidth(this, 800);", source);
            Assert.Contains("global::Avalonia.Controls.Design.SetHeight(this, 600);", source);
            Assert.DoesNotContain("#if DEBUG", source);
            Assert.DoesNotContain("#endif", source);
        }
    }
}
