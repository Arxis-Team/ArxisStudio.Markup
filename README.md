# ArxisStudio.Markup

`ArxisStudio.Markup` — набор библиотек для model-driven описания Avalonia UI через документы `.arxui`, их сериализации, runtime-загрузки и compile-time генерации кода.

Проект ориентирован на визуальный конструктор, поэтому основной приоритет — чистый и предсказуемый публичный API.

## Текущая архитектура

- `ArxisStudio.Markup` содержит только runtime-модель (`UiDocument`, `UiNode`, `UiValue`).
- `$design` удалён из runtime-схемы `.arxui`.
- Метаданные дизайнера вынесены в `ArxisStudio.Markup.Metadata` (`DesignOverlay`).
- JSON-кодек для метаданных находится в `ArxisStudio.Markup.Metadata.Json`.
- Интеграция с редактором дизайна реализована в `ArxisStudio.Markup.DesignEditorBridge`.
- `ArxisStudio.DesignEditor` подключается как submodule и используется через bridge-слой.

## Состав решения

### `ArxisStudio.Markup`

Независимый контракт runtime-модели документа:
- `UiDocument`, `UiNode`, `UiValue`
- `UiStyles`, `UiResources`
- binding/resource/asset-описания

Пакет не зависит от Avalonia и инфраструктуры редактора.

### `ArxisStudio.Markup.Json`

JSON codec для `.arxui`:
- `ArxuiSerializer.Deserialize(string json)`
- `ArxuiSerializer.Serialize(UiDocument document)`

### `ArxisStudio.Markup.Json.Loader`

Runtime builder из `UiNode` в дерево Avalonia-контролов:
- `ArxuiLoader.Load(UiNode node, ArxuiLoadContext context)`
- расширение через `ITypeResolver`, `IAssetResolver`, `IMarkupDocumentResolver`, `IPathResolver`, `ITopLevelControlFactory`

### `ArxisStudio.Markup.Metadata`

Контракт метаданных дизайнера:
- `DesignOverlay`
- `IDesignPropertyRegistry`
- `IMetadataValidator`

### `ArxisStudio.Markup.Metadata.Json`

JSON-сериализация метаданных дизайнера:
- `DesignOverlaySerializer.Deserialize(string json)`
- `DesignOverlaySerializer.Serialize(DesignOverlay overlay)`

### `ArxisStudio.Markup.DesignEditorBridge`

Bridge между метаданными и `ArxisStudio.DesignEditor`:
- `DesignEditorBridgeRuntime`
- `DesignOverlayApplier`
- `DesignOverlayExtractor`
- реестры свойств/апплаеров/ридеров

### `ArxisStudio.Markup.Generator`

Roslyn source generator для `InitializeComponent()` по `.arxui`:
- валидация документа
- генерация кода
- диагностики `ADG*`

## Принципы API

1. Runtime-модель изолирована от design-time инфраструктуры.
2. Метаданные дизайнера не «размазаны» по core-модели.
3. Расширение выполняется через реестры и интерфейсы, а не через hardcode в ядре.
4. Публичное поведение должно быть диагностируемым и стабильным.

## Быстрый старт

### 1) Десериализация `.arxui`

```csharp
using ArxisStudio.Markup.Json;

var json = File.ReadAllText("Views/MainWindow.arxui");
var document = ArxuiSerializer.Deserialize(json);
```

### 2) Preview через loader

```csharp
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Services;

var loader = new ArxuiLoader();
var control = loader.Load(
    document!.Root,
    new ArxuiLoadContext
    {
        TypeResolver = new ReflectionTypeResolver(),
        AssetResolver = new DefaultAssetResolver(),
        PathResolver = new ProjectContextPathResolver(projectDirectory, assemblyName),
        TopLevelControlFactory = new DefaultTopLevelControlFactory()
    });
```

### 3) Работа с метаданными дизайнера

```csharp
using ArxisStudio.Markup.DesignEditorBridge;

var runtime = DesignEditorBridgeRuntime.CreateDefault();
var applyDiagnostics = runtime.Apply(overlay, controlMap);
var extractedOverlay = runtime.Extract(controlMap);
var metadataDiagnostics = runtime.Validate(overlay);
```

### 4) Подключение generator

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.arxui" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\ArxisStudio.Markup.Generator\ArxisStudio.Markup.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Сборка и тесты

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Tests/ArxisStudio.Markup.Generator.Tests.csproj
```
