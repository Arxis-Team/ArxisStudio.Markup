# API-документация ArxisStudio.Markup

Этот раздел описывает публичный API библиотек решения `ArxisStudio.Markup`.
Во всех разделах ниже добавлены примеры файлов `.arxui` для практического использования API.

## Состав библиотек

1. [ArxisStudio.Markup](./api-markup-core.md)  
   Базовая модель `.arxui`: `UiDocument`, `UiNode`, `UiValue`, binding, ресурсы и стили.
2. [ArxisStudio.Markup.Json](./api-markup-json.md)  
   Сериализация и десериализация `.arxui` в JSON.
3. [ArxisStudio.Markup.Json.Loader](./api-markup-json-loader.md)  
   Построение дерева `Avalonia Control` из модели `UiNode`.
4. [ArxisStudio.Markup.Metadata](./api-markup-metadata.md)  
   Модель design-time overlay и контракты валидации.
5. [ArxisStudio.Markup.Metadata.Json](./api-markup-metadata-json.md)  
   JSON-сериализация `DesignMetadata`.
6. [ArxisStudio.Markup.Generator](./api-generator.md)  
   Roslyn incremental generator для `.arxui`.

## Быстрый старт

```csharp
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Services;

var document = ArxuiSerializer.Deserialize(File.ReadAllText("MainView.arxui"));
if (document is null)
{
    throw new InvalidOperationException("Документ не содержит Root.");
}

var loader = new ArxuiLoader();
var context = new ArxuiLoadContext
{
    TypeResolver = new ReflectionTypeResolver(),
    AssetResolver = new DefaultAssetResolver()
};

var rootControl = loader.Load(document.Root, context);
```

## Связанные документы

- [Inline `$design` Workflow](./inline-design-workflow.md)
- [Формат metadata](./metadata-format.md)
- [Миграция: `DesignMetadata` -> inline `$design`](./migration-design-overlay.md)


