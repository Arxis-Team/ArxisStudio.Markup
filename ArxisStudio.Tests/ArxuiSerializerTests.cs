using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using Newtonsoft.Json;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    /// <summary>
    /// Тесты сериализации и десериализации документов <c>.arxui</c>.
    /// </summary>
    public class ArxuiSerializerTests
    {
        private const string JsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""Sample.Views.ProfileView"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Styles"": [
      {
        ""$styleInclude"": ""avares://Sample.Assembly/Styles/ProfileView.axaml""
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
      ""NodeSelector"": {
        ""TypeName"": ""Avalonia.Controls.Border"",
        ""Properties"": {
          ""Name"": ""SelectorBorder""
        }
      }
    },
    ""Properties"": {
      ""Width"": 320,
      ""Background"": {
        ""$resource"": ""BackgroundBrush""
      },
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.Image"",
        ""Properties"": {
          ""Source"": {
            ""$asset"": {
              ""Path"": ""/Assets/avatar.png"",
              ""Assembly"": ""Sample.Assembly""
            }
          }
        }
      },
      ""Tag"": {
        ""$binding"": ""UserName"",
        ""Mode"": ""TwoWay"",
        ""StringFormat"": ""Hello {0}"",
        ""RelativeSource"": {
          ""Mode"": ""Self""
        }
      }
    }
  }
}
";

        /// <summary>
        /// Проверяет чтение специальных видов значений из JSON.
        /// </summary>
        [Fact]
        public void Deserialize_should_read_special_value_kinds()
        {
            var document = ArxuiSerializer.Deserialize(JsonModel);

            Assert.NotNull(document);
            Assert.Equal(1, document!.SchemaVersion);
            Assert.Equal(UiDocumentKind.Control, document.Kind);
            Assert.Equal("Sample.Views.ProfileView", document.Class);
            Assert.Equal("Avalonia.Controls.UserControl", document.Root.TypeName);
            Assert.NotNull(document.Root.Styles);
            Assert.Equal(2, document.Root.Styles!.Items.Count);
            var styleInclude = Assert.IsType<StyleIncludeValue>(document.Root.Styles.Items[0]);
            Assert.Equal("avares://Sample.Assembly/Styles/ProfileView.axaml", styleInclude.Source);
            var inlineStyle = Assert.IsType<StyleNodeValue>(document.Root.Styles.Items[1]);
            Assert.Equal("Avalonia.Styling.Style", inlineStyle.Node.TypeName);
            Assert.NotNull(document.Root.Resources);
            Assert.Single(document.Root.Resources!.MergedDictionaries);
            Assert.Equal("avares://Sample.Assembly/Resources/Common.axaml", document.Root.Resources.MergedDictionaries[0].Source);
            var nodeSelector = Assert.IsType<NodeValue>(document.Root.Resources.Values["NodeSelector"]);
            Assert.Equal("Avalonia.Controls.Border", nodeSelector.Node.TypeName);

            var background = Assert.IsType<ResourceValue>(document.Root.Properties["Background"]);
            Assert.Equal("BackgroundBrush", background.Key);

            var content = Assert.IsType<NodeValue>(document.Root.Properties["Content"]);
            var source = Assert.IsType<UriReferenceValue>(content.Node.Properties["Source"]);
            Assert.Equal("/Assets/avatar.png", source.Path);
            Assert.Equal("Sample.Assembly", source.Assembly);

            var tag = Assert.IsType<BindingValue>(document.Root.Properties["Tag"]);
            Assert.Equal("UserName", tag.Binding.Path);
            Assert.Equal(BindingMode.TwoWay, tag.Binding.Mode);
            Assert.Equal("Hello {0}", tag.Binding.StringFormat);
            Assert.NotNull(tag.Binding.RelativeSource);
            Assert.Equal(RelativeSourceMode.Self, tag.Binding.RelativeSource!.Mode);
        }

        /// <summary>
        /// Проверяет корректность round-trip сериализации специальных значений.
        /// </summary>
        [Fact]
        public void Serialize_should_round_trip_special_value_kinds()
        {
            var original = ArxuiSerializer.Deserialize(JsonModel);

            var serialized = ArxuiSerializer.Serialize(original!);
            var roundTripped = ArxuiSerializer.Deserialize(serialized);

            Assert.NotNull(roundTripped);
            Assert.Equal(original!.SchemaVersion, roundTripped!.SchemaVersion);
            Assert.Equal(original.Kind, roundTripped.Kind);
            Assert.Equal(original.Class, roundTripped.Class);
            Assert.Equal(original.Root.TypeName, roundTripped.Root.TypeName);
            Assert.NotNull(roundTripped.Root.Styles);
            Assert.Equal(2, roundTripped.Root.Styles!.Items.Count);
            var roundTrippedStyleInclude = Assert.IsType<StyleIncludeValue>(roundTripped.Root.Styles.Items[0]);
            Assert.Equal("avares://Sample.Assembly/Styles/ProfileView.axaml", roundTrippedStyleInclude.Source);
            Assert.NotNull(roundTripped.Root.Resources);
            Assert.Single(roundTripped.Root.Resources!.MergedDictionaries);
            Assert.Equal("avares://Sample.Assembly/Resources/Common.axaml", roundTripped.Root.Resources.MergedDictionaries[0].Source);
            var roundTrippedNodeSelector = Assert.IsType<NodeValue>(roundTripped.Root.Resources.Values["NodeSelector"]);
            Assert.Equal("Avalonia.Controls.Border", roundTrippedNodeSelector.Node.TypeName);

            var roundTrippedBackground = Assert.IsType<ResourceValue>(roundTripped.Root.Properties["Background"]);
            Assert.Equal("BackgroundBrush", roundTrippedBackground.Key);

            var roundTrippedContent = Assert.IsType<NodeValue>(roundTripped.Root.Properties["Content"]);
            var roundTrippedSource = Assert.IsType<UriReferenceValue>(roundTrippedContent.Node.Properties["Source"]);
            Assert.Equal("/Assets/avatar.png", roundTrippedSource.Path);
            Assert.Equal("Sample.Assembly", roundTrippedSource.Assembly);

            var roundTrippedTag = Assert.IsType<BindingValue>(roundTripped.Root.Properties["Tag"]);
            Assert.Equal("UserName", roundTrippedTag.Binding.Path);
            Assert.Equal(BindingMode.TwoWay, roundTrippedTag.Binding.Mode);
            Assert.Equal("Hello {0}", roundTrippedTag.Binding.StringFormat);
            Assert.NotNull(roundTrippedTag.Binding.RelativeSource);
            Assert.Equal(RelativeSourceMode.Self, roundTrippedTag.Binding.RelativeSource!.Mode);
            Assert.Contains(@"""$resource"": ""BackgroundBrush""", serialized);
            Assert.Contains(@"""$binding"": ""UserName""", serialized);
            Assert.Contains(@"""$asset"": {", serialized);
            Assert.Contains(@"""Assembly"": ""Sample.Assembly""", serialized);
            Assert.Contains(@"""$styleInclude"": ""avares://Sample.Assembly/Styles/ProfileView.axaml""", serialized);
            Assert.Contains(@"""$mergedDictionaries"": [", serialized);
        }

        /// <summary>
        /// Проверяет поддержку устаревшей формы корневых свойств.
        /// </summary>
        [Fact]
        public void Deserialize_should_support_legacy_root_properties_shape()
        {
            const string legacyJson = @"
{
  ""SchemaVersion"": 1,
  ""AssetType"": ""Window"",
  ""Properties"": {
    ""Title"": ""Legacy Window""
  }
}
";

            var document = ArxuiSerializer.Deserialize(legacyJson);

            Assert.NotNull(document);
            Assert.Equal(UiDocumentKind.Window, document!.Kind);
            Assert.Null(document.Class);
            Assert.Equal("Avalonia.Controls.Window", document.Root.TypeName);
            var title = Assert.IsType<ScalarValue>(document.Root.Properties["Title"]);
            Assert.Equal("Legacy Window", title.Value);
        }

        /// <summary>
        /// Проверяет отклонение неподдерживаемого типа документа.
        /// </summary>
        [Fact]
        public void Deserialize_should_reject_unsupported_document_kind()
        {
            const string invalidJson = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""UserControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {}
  }
}
";

            Assert.Throws<JsonSerializationException>(() => ArxuiSerializer.Deserialize(invalidJson));
        }

        /// <summary>
        /// Проверяет чтение и сериализацию inline-поля <c>$design</c> на уровне документа и узла.
        /// </summary>
        [Fact]
        public void Serialize_and_deserialize_should_support_inline_design()
        {
            const string jsonWithDesign = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""$design"": {
    ""SurfaceWidth"": 1280,
    ""SurfaceHeight"": 720
  },
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.Canvas"",
    ""Properties"": {
      ""Children"": [
        {
          ""TypeName"": ""Avalonia.Controls.Border"",
          ""$design"": {
            ""Layout.X"": 100,
            ""Layout.Y"": 220,
            ""DesignInteraction.MovePolicy"": ""Both""
          },
          ""Properties"": {}
        }
      ]
    }
  }
}
";

            var document = ArxuiSerializer.Deserialize(jsonWithDesign);

            Assert.NotNull(document);
            Assert.NotNull(document!.Design);
            Assert.True(document.Design!.Properties.ContainsKey("SurfaceWidth"));

            var children = Assert.IsType<CollectionValue>(document.Root.Properties["Children"]);
            var childNode = Assert.IsType<NodeValue>(children.Items[0]).Node;
            Assert.NotNull(childNode.Design);
            Assert.True(childNode.Design!.Properties.ContainsKey("Layout.X"));

            var serialized = ArxuiSerializer.Serialize(document);
            Assert.Contains(@"""$design"": {", serialized);
            Assert.Contains(@"""SurfaceWidth"": 1280", serialized);
            Assert.Contains(@"""Layout.X"": 100", serialized);
        }
    }
}
