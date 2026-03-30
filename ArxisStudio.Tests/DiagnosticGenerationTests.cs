using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    /// <summary>
    /// Тесты диагностик source generator для документов <c>.arxui</c>.
    /// </summary>
    public class DiagnosticGenerationTests
    {
        private const string PartialUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Views
{
    public partial class ValidControl : UserControl
    {
    }
}
";

        private const string NonPartialUserControlSource = @"
using Avalonia.Controls;

namespace TestApp.Views
{
    public class NonPartialControl : UserControl
    {
    }
}
";

        private const string PartialApplicationSource = @"
using Avalonia;

namespace TestApp
{
    public partial class ValidApp : Application
    {
    }
}
";

        /// <summary>
        /// Проверяет выдачу диагностики ADG0005 при отсутствии свойства Class.
        /// </summary>
        [Fact]
        public void Missing_class_should_report_ADG0005()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0005" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет выдачу диагностики ADG0006 для неизвестного класса.
        /// </summary>
        [Fact]
        public void Unknown_class_should_report_ADG0006()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.DoesNotExist"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0006" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет выдачу диагностики ADG0007 для неполного класса.
        /// </summary>
        [Fact]
        public void Non_partial_class_should_report_ADG0007()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.NonPartialControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                NonPartialUserControlSource,
                "NonPartialControl.arxui.cs",
                ("NonPartialControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0007" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет выдачу диагностики ADG0008 при несовместимом Kind.
        /// </summary>
        [Fact]
        public void Incompatible_kind_should_report_ADG0008()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Window"",
  ""Class"": ""TestApp.Views.ValidControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.Window"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0008" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет выдачу ADG0009 для каждого дублирующего ассета.
        /// </summary>
        [Fact]
        public void Duplicate_class_should_report_ADG0009_for_each_asset()
        {
            const string json1 = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.ValidControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            const string json2 = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.ValidControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json1),
                ("AnotherValidControl.arxui", json2));

            Assert.Equal(2, diagnostics.Count(d => d.Id == "ADG0009" && d.Severity == DiagnosticSeverity.Error));
        }

        /// <summary>
        /// Проверяет выдачу диагностики ADG0010 при несовместимом корневом типе для Kind.
        /// </summary>
        [Fact]
        public void Incompatible_root_kind_should_report_ADG0010()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Window"",
  ""Class"": ""TestApp.Views.ValidControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0010" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет выдачу диагностики ADG0011 при несовместимости Root и target-класса.
        /// </summary>
        [Fact]
        public void Incompatible_root_target_class_should_report_ADG0011()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""TestApp.Views.ValidControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.Border"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("ValidControl.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0011" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет отсутствие ADG0005 для документа стилей без Class.
        /// </summary>
        [Fact]
        public void Styles_without_class_should_not_report_ADG0005()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Styles"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Styling.Styles"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialUserControlSource,
                "ValidControl.arxui.cs",
                ("Styles.arxui", json));

            Assert.DoesNotContain(diagnostics, d => d.Id == "ADG0005");
        }

        /// <summary>
        /// Проверяет выдачу ADG0005 для документа приложения без Class.
        /// </summary>
        [Fact]
        public void Application_with_missing_class_should_report_ADG0005()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Application"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Application"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialApplicationSource,
                "App.arxui.cs",
                ("App.arxui", json));

            Assert.Contains(diagnostics, d => d.Id == "ADG0005" && d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Проверяет отсутствие несовместимых диагностик Kind при корректном приложении.
        /// </summary>
        [Fact]
        public void Application_with_matching_class_should_not_report_kind_diagnostics()
        {
            const string json = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Application"",
  ""Class"": ""TestApp.ValidApp"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Application"",
    ""Properties"": {}
  }
}
";

            var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                PartialApplicationSource,
                "App.arxui.cs",
                ("App.arxui", json));

            Assert.DoesNotContain(diagnostics, d => d.Id is "ADG0008" or "ADG0010" or "ADG0011");
        }
    }
}
