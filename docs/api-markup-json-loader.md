# ArxisStudio.Markup.Json.Loader

Пакет конвертирует `UiNode` в дерево Avalonia-контролов для preview/runtime-сценариев.

## Основные типы

### `ArxuiLoader`

- `Control? Load(UiNode node, ArxuiLoadContext context)` — строит дерево контролов из узла.

### `ArxuiLoadContext`

- `TypeResolver: ITypeResolver` (обязательно)
- `AssetResolver: IAssetResolver?`
- `DocumentResolver: IMarkupDocumentResolver?`
- `PathResolver: IPathResolver?`
- `TopLevelControlFactory: ITopLevelControlFactory?`
- `NodeMap: IDictionary<UiNode, Control>?`
- `Options: ArxuiLoadOptions`

### `ArxuiLoadOptions`

- `AllowBindings`
- `AllowAssets` (по умолчанию `true`)
- `AllowExternalIncludes` (по умолчанию `true`)
- `AllowDocumentFallback` (по умолчанию `true`)

## Абстракции расширения

- `ITypeResolver` — резолв типов по имени.
- `IAssetResolver` — резолв `UriReferenceValue` в объект свойства.
- `IMarkupDocumentResolver` — fallback к другому `.arxui` по `UiDocument.Class`.
- `IPathResolver` — преобразование относительного/assembly пути в файловый.
- `ITopLevelControlFactory` — preview top-level типов (например, `Window`).
- `TopLevelControlBuildResult(Control Root, ContentControl? ContentHost)` — результат фабрики top-level.

## Готовые реализации

- `ReflectionTypeResolver`
- `DefaultAssetResolver`
- `ProjectContextPathResolver`
- `ProjectMarkupDocumentResolver`
- `DefaultTopLevelControlFactory`

## Пример: загрузка документа в контрол

```csharp
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Services;

var document = ArxuiSerializer.Deserialize(File.ReadAllText("MainView.arxui"))
    ?? throw new InvalidOperationException("Root не найден.");

var nodeMap = new Dictionary<ArxisStudio.Markup.UiNode, Avalonia.Controls.Control>();

var context = new ArxuiLoadContext
{
    TypeResolver = new ReflectionTypeResolver(),
    AssetResolver = new DefaultAssetResolver(),
    PathResolver = new ProjectContextPathResolver(
        projectDirectory: @"C:\Work\DemoApp",
        assemblyName: "DemoApp"),
    NodeMap = nodeMap,
    Options = new ArxuiLoadOptions
    {
        AllowBindings = true,
        AllowAssets = true,
        AllowExternalIncludes = true,
        AllowDocumentFallback = true
    }
};

var loader = new ArxuiLoader();
var root = loader.Load(document.Root, context);
```

## Пример: свой `ITypeResolver`

```csharp
using ArxisStudio.Markup.Json.Loader.Abstractions;

public sealed class MyTypeResolver : ITypeResolver
{
    public Type? Resolve(string typeName)
    {
        if (typeName == "MyButton")
        {
            return typeof(Avalonia.Controls.Button);
        }

        return Type.GetType(typeName);
    }
}
```

## Пример `.arxui` для `ArxuiLoader`

Файл `PreviewWindow.arxui`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Window",
  "Class": "Demo.Views.PreviewWindow",
  "Root": {
    "TypeName": "Avalonia.Controls.Window",
    "Properties": {
      "Title": "Preview",
      "Content": {
        "TypeName": "Avalonia.Controls.Border",
        "Properties": {
          "Padding": "16",
          "Child": {
            "TypeName": "Avalonia.Controls.TextBlock",
            "Properties": {
              "Text": "Rendered by ArxuiLoader"
            }
          }
        }
      }
    }
  }
}
```

Этот пример показывает top-level документ (`Kind = Window`) и путь, где используется `ITopLevelControlFactory`.
