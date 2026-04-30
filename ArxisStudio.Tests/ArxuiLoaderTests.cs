using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Abstractions;
using ArxisStudio.Markup.Json.Loader.Services;
using Xunit;

namespace ArxisStudio.Markup.Generator.Tests
{
    /// <summary>
    /// Тесты runtime/preview-загрузчика <c>.arxui</c>-деревьев.
    /// </summary>
    public class ArxuiLoaderTests
    {
        /// <summary>
        /// Проверяет построение вложенного дерева, коллекций, CSS-классов и attached properties.
        /// </summary>
        [Fact]
        public void Load_should_build_tree_collections_classes_and_attached_properties()
        {
            var node = new UiNode(
                "Avalonia.Controls.Grid",
                new Dictionary<string, UiValue>
                {
                    ["RowDefinitions"] = new ScalarValue("Auto,*"),
                    ["Children"] = new CollectionValue(new UiValue[]
                    {
                        new NodeValue(new UiNode(
                            "Avalonia.Controls.TextBlock",
                            new Dictionary<string, UiValue>
                            {
                                ["Text"] = new ScalarValue("Header"),
                                ["Classes"] = new ScalarValue("title highlighted"),
                                ["Avalonia.Controls.Grid.Row"] = new ScalarValue(1)
                            }))
                    })
                });

            var control = Load(node);

            var grid = Assert.IsType<Grid>(control);
            Assert.Equal(2, grid.RowDefinitions.Count);
            var child = Assert.IsType<TextBlock>(Assert.Single(grid.Children));
            Assert.Equal("Header", child.Text);
            Assert.Contains("title", child.Classes);
            Assert.Contains("highlighted", child.Classes);
            Assert.Equal(1, Grid.GetRow(child));
        }

        /// <summary>
        /// Проверяет, что локальные ресурсы доступны через <c>$resource</c>.
        /// </summary>
        [Fact]
        public void Load_should_apply_local_resource_values()
        {
            var node = new UiNode(
                "Avalonia.Controls.Border",
                new Dictionary<string, UiValue>
                {
                    ["Tag"] = new ResourceValue("Greeting")
                },
                Resources: new UiResources(
                    Array.Empty<UiResourceDictionaryInclude>(),
                    new Dictionary<string, UiValue>
                    {
                        ["Greeting"] = new ScalarValue("Hello from resource")
                    }));

            var control = Load(node);

            var border = Assert.IsType<Border>(control);
            Assert.Equal("Hello from resource", border.Tag);
            Assert.Equal("Hello from resource", border.Resources["Greeting"]);
        }

        /// <summary>
        /// Проверяет, что настройка <see cref="ArxuiLoadOptions.AllowAssets"/> блокирует разрешение ассетов.
        /// </summary>
        [Fact]
        public void Load_should_not_resolve_assets_when_assets_are_disabled()
        {
            var assetResolver = new CountingAssetResolver();
            var node = new UiNode(
                "Avalonia.Controls.Image",
                new Dictionary<string, UiValue>
                {
                    ["Source"] = new UriReferenceValue("/Assets/avatar.png")
                });

            var control = Load(
                node,
                new ArxuiLoadContext
                {
                    TypeResolver = new ReflectionTypeResolver(),
                    AssetResolver = assetResolver,
                    Options = new ArxuiLoadOptions { AllowAssets = false }
                });

            var image = Assert.IsType<Image>(control);
            Assert.Null(image.Source);
            Assert.Equal(0, assetResolver.CallCount);
        }

        /// <summary>
        /// Проверяет fallback на другой <c>.arxui</c>-документ, если CLR-тип не найден.
        /// </summary>
        [Fact]
        public void Load_should_fallback_to_arxui_backed_control_when_type_is_unknown()
        {
            var requestedNode = new UiNode(
                "TestApp.Views.ProfileCard",
                new Dictionary<string, UiValue>());

            var fallbackRoot = new UiNode(
                "Avalonia.Controls.Border",
                new Dictionary<string, UiValue>
                {
                    ["Child"] = new NodeValue(new UiNode(
                        "Avalonia.Controls.TextBlock",
                        new Dictionary<string, UiValue>
                        {
                            ["Text"] = new ScalarValue("Fallback content")
                        }))
                });

            var nodeMap = new Dictionary<UiNode, Control>();
            var control = Load(
                requestedNode,
                new ArxuiLoadContext
                {
                    TypeResolver = new ReflectionTypeResolver(),
                    DocumentResolver = new SingleDocumentResolver("TestApp.Views.ProfileCard", fallbackRoot),
                    NodeMap = nodeMap
                });

            var border = Assert.IsType<Border>(control);
            var textBlock = Assert.IsType<TextBlock>(border.Child);
            Assert.Equal("Fallback content", textBlock.Text);
            Assert.Same(border, nodeMap[requestedNode]);
        }

        /// <summary>
        /// Проверяет resilient error UI для неизвестного типа без fallback-документа.
        /// </summary>
        [Fact]
        public void Load_should_return_error_text_block_for_unknown_type_without_fallback()
        {
            var node = new UiNode(
                "TestApp.Views.MissingControl",
                new Dictionary<string, UiValue>());

            var control = Load(node);

            var textBlock = Assert.IsType<TextBlock>(control);
            Assert.Contains("Type 'TestApp.Views.MissingControl' not found", textBlock.Text);
        }

        private static Control? Load(UiNode node)
        {
            return Load(
                node,
                new ArxuiLoadContext
                {
                    TypeResolver = new ReflectionTypeResolver()
                });
        }

        private static Control? Load(UiNode node, ArxuiLoadContext context)
        {
            return new ArxuiLoader().Load(node, context);
        }

        private sealed class CountingAssetResolver : IAssetResolver
        {
            public int CallCount { get; private set; }

            public object? Resolve(UriReferenceValue asset, Type targetType, ArxuiLoadContext context)
            {
                CallCount++;
                return null;
            }
        }

        private sealed class SingleDocumentResolver : IMarkupDocumentResolver
        {
            private readonly string _className;
            private readonly UiNode _root;

            public SingleDocumentResolver(string className, UiNode root)
            {
                _className = className;
                _root = root;
            }

            public UiNode? ResolveRootByClass(string className)
            {
                return string.Equals(className, _className, StringComparison.Ordinal)
                    ? _root
                    : null;
            }
        }
    }
}
