using System.Linq;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using Newtonsoft.Json;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    public class ArxuiSerializerTests
    {
        private const string JsonModel = @"
{
  ""SchemaVersion"": 1,
  ""Kind"": ""Control"",
  ""Class"": ""Sample.Views.ProfileView"",
  ""$design"": {
    ""SurfaceWidth"": 1440,
    ""SurfaceHeight"": 900
  },
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""$design"": {
      ""Locked"": false,
      ""IgnorePreviewInput"": true,
      ""AllowMove"": true,
      ""AllowResize"": true
    },
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
        ""$design"": {
          ""Hidden"": false
        },
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

        [Fact]
        public void Deserialize_should_read_special_value_kinds()
        {
            var document = ArxuiSerializer.Deserialize(JsonModel);

            Assert.NotNull(document);
            Assert.Equal(1, document!.SchemaVersion);
            Assert.Equal(UiDocumentKind.Control, document.Kind);
            Assert.Equal("Sample.Views.ProfileView", document.Class);
            Assert.NotNull(document.Design);
            Assert.Equal(1440, document.Design!.SurfaceWidth);
            Assert.Equal(900, document.Design.SurfaceHeight);
            Assert.Equal("Avalonia.Controls.UserControl", document.Root.TypeName);
            Assert.NotNull(document.Root.Design);
            Assert.False(document.Root.Design!.Locked);
            Assert.True(document.Root.Design.IgnorePreviewInput);
            Assert.True(document.Root.Design.AllowMove);
            Assert.True(document.Root.Design.AllowResize);
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
            Assert.NotNull(content.Node.Design);
            Assert.False(content.Node.Design!.Hidden);
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
            Assert.NotNull(roundTripped.Design);
            Assert.Equal(1440, roundTripped.Design!.SurfaceWidth);
            Assert.Equal(900, roundTripped.Design.SurfaceHeight);
            Assert.Equal(original.Root.TypeName, roundTripped.Root.TypeName);
            Assert.NotNull(roundTripped.Root.Design);
            Assert.True(roundTripped.Root.Design!.IgnorePreviewInput);
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
            Assert.NotNull(roundTrippedContent.Node.Design);
            Assert.False(roundTrippedContent.Node.Design!.Hidden);
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
            Assert.Contains(@"""$design"": {", serialized);
            Assert.Contains(@"""SurfaceWidth"": 1440.0", serialized);
            Assert.Contains(@"""IgnorePreviewInput"": true", serialized);
        }

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
    }
}
