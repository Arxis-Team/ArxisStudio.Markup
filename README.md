# ArxisStudio.Markup

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-11.x-2E8B57)
![Status](https://img.shields.io/badge/status-pre--MVP-orange)

`ArxisStudio.Markup` — набор библиотек для model-driven описания Avalonia UI через `.arxui` документы, отдельные design-time metadata и интеграцию с визуальным редактором.

## Status

Проект находится в pre-MVP стадии.

- `SchemaVersion` для `.arxui`: `1`
- обратная совместимость формата сейчас не гарантируется
- публичный API проектируется с приоритетом чистой архитектуры и расширяемости

## Архитектурные принципы

1. Runtime UI-модель и design-time metadata разделены.
2. `.arxui` содержит только runtime-структуру.
3. Design metadata живут в `DesignOverlay`.
4. Интеграция с редактором идет через Bridge-слой, а не через хардкод в core.

## Package Map

- `ArxisStudio.Markup`  
  Core-модель: `UiDocument`, `UiNode`, `UiValue`.
- `ArxisStudio.Markup.Json`  
  JSON serializer/deserializer для `.arxui`.
- `ArxisStudio.Markup.Json.Loader`  
  Построение `Avalonia.Controls.Control` из `UiNode`.
- `ArxisStudio.Markup.Metadata`  
  Модель `DesignOverlay`, контракты registry/validator.
- `ArxisStudio.Markup.Metadata.Json`  
  JSON codec для `DesignOverlay`.
- `ArxisStudio.Markup.DesignEditorBridge`  
  Apply/Extract/Validate metadata между overlay и control tree.
- `ArxisStudio.Markup.Generator`  
  Roslyn incremental generator (`InitializeComponent()` из `.arxui`).

## Пример `.arxui`

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "Demo.Views.MainView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.TextBlock",
        "Properties": {
          "Text": "Hello from .arxui"
        }
      }
    }
  }
}
```

## Quick Start

### 1. Deserialize `.arxui`

```csharp
using ArxisStudio.Markup.Json;

var doc = ArxuiSerializer.Deserialize(File.ReadAllText("Views/MainView.arxui"));
if (doc is null)
{
    throw new InvalidOperationException("Root node is missing.");
}
```

### 2. Build preview with loader

```csharp
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Services;

var loader = new ArxuiLoader();
var root = loader.Load(doc.Root, new ArxuiLoadContext
{
    TypeResolver = new ReflectionTypeResolver(),
    AssetResolver = new DefaultAssetResolver()
});
```

### 3. Apply/Extract metadata through bridge

```csharp
using ArxisStudio.Markup.DesignEditorBridge;

var runtime = DesignEditorBridgeRuntime.CreateDefault();
var validation = runtime.Validate(overlay);
var applyDiagnostics = runtime.Apply(overlay, controlMap);
var extracted = runtime.Extract(controlMap);
```

### 4. Connect source generator

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

## Documentation

- [Docs Index](./docs/README.md)
- [API Index](./docs/api-index.md)
- [Metadata Format](./docs/metadata-format.md)
- [DesignEditor Bridge](./docs/design-editor-bridge.md)
- [Migration: `$design` -> `DesignOverlay`](./docs/migration-design-overlay.md)

## Compatibility

- Target platform: `.NET 9`
- UI stack: `Avalonia 11`

## Roadmap (Pre-MVP)

1. Stabilize public API boundaries for core/metadata/bridge packages.
2. Move from predefined hardcoded mappings to registry-first configuration where needed.
3. Complete end-to-end integration with `ArxisStudio.DesignEditor` (submodule).
4. Add scenario tests for round-trip: `.arxui` -> loader -> bridge extract/apply -> metadata JSON.
5. Publish package versioning and release policy after MVP baseline.

## Build & Test

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Tests/ArxisStudio.Markup.Generator.Tests.csproj
```

## Contributing

PR и issue приветствуются.

Перед отправкой изменений:

1. Проверьте сборку решения.
2. Обновите документацию в `docs/`, если меняется публичный API.
3. Для изменений формата `.arxui`/metadata добавьте примеры и миграционные заметки.
