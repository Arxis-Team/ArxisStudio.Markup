# ArxisStudio.Markup

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.x-2E8B57)
![Status](https://img.shields.io/badge/status-pre--MVP-orange)

`ArxisStudio.Markup` — набор библиотек для model-driven описания Avalonia UI через `.arxui`, включая inline-поле `$design` и генерацию кода.

## Архитектура

1. Runtime UI и design-time данные хранятся в одном `.arxui` через чистую модель:
   `UiDocument` / `UiNode` + `UiDesignData`.
2. Для преобразования inline `$design` <-> `DesignMetadata` используется
   `InlineDesignMetadataConverter`.

## Package Map

- `ArxisStudio.Markup` — core-модель (`UiDocument`, `UiNode`, `UiValue`, `UiDesignData`).
- `ArxisStudio.Markup.Json` — сериализация/десериализация `.arxui` (включая `$design`).
- `ArxisStudio.Markup.Json.Loader` — построение `Control`-дерева из `UiNode`.
- `ArxisStudio.Markup.Metadata` — `DesignMetadata`, validator, `InlineDesignMetadataConverter`.
- `ArxisStudio.Markup.Metadata.Json` — JSON codec для `DesignMetadata`.
- `ArxisStudio.Markup.Generator` — Roslyn incremental generator.

## Пример `.arxui` с `$design`

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "$design": {
    "SurfaceWidth": 1280,
    "SurfaceHeight": 720
  },
  "Class": "Demo.Views.EditorSurface",
  "Root": {
    "TypeName": "Avalonia.Controls.Canvas",
    "Properties": {
      "Children": [
        {
          "TypeName": "Avalonia.Controls.Border",
          "$design": {
            "Layout.X": 120,
            "Layout.Y": 240,
            "DesignInteraction.MovePolicy": "Both"
          },
          "Properties": {
            "Width": 180,
            "Height": 80
          }
        }
      ]
    }
  }
}
```

## Quick Start

```csharp
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Metadata;

var document = ArxuiSerializer.Deserialize(File.ReadAllText("MainView.arxui"))
    ?? throw new InvalidOperationException("Документ не содержит Root.");

var metadata = InlineDesignMetadataConverter.FromInlineDesign(document);
var updatedDocument = InlineDesignMetadataConverter.ApplyOverlay(document, metadata);
File.WriteAllText("MainView.arxui", ArxuiSerializer.Serialize(updatedDocument));
```

## Documentation

- [Docs Index](./docs/README.md)
- [API Index](./docs/api-index.md)
- [Inline `$design` Workflow](./docs/inline-design-workflow.md)

## Build & Test

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Tests/ArxisStudio.Markup.Generator.Tests.csproj
```

