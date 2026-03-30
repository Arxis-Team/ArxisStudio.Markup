# ArxisStudio.Markup.Metadata.Json

Пакет сериализует и десериализует `DesignOverlay` в JSON.

## Публичный API

### `DesignOverlaySerializer`

- `DesignOverlay Deserialize(string json)` — чтение overlay из JSON.
- `string Serialize(DesignOverlay overlay)` — запись overlay в JSON.

## Модель сериализации

- Объект верхнего уровня содержит `Document` и `Nodes`.
- `Nodes` — объект, где ключом выступает `NodeRef.Value`.
- Поддерживаются скаляры, объекты и массивы (`DesignScalarValue`, `DesignObjectValue`, `DesignCollectionValue`).

## Пример: запись в JSON

```csharp
using ArxisStudio.Markup.Metadata;
using ArxisStudio.Markup.Metadata.Json;

var overlay = new DesignOverlay(
    Document: null,
    Nodes: new Dictionary<NodeRef, NodeDesignData>
    {
        [new NodeRef("/Root/Children/0")] = new NodeDesignData(
            new Dictionary<string, DesignValue>
            {
                ["Avalonia.Input.InputElement.IsHitTestVisible"] = new DesignScalarValue(false),
                ["ArxisStudio.Attached.Layout.X"] = new DesignScalarValue(120.0)
            })
    });

var json = DesignOverlaySerializer.Serialize(overlay);
File.WriteAllText("MainView.design.json", json);
```

## Пример: чтение из JSON

```csharp
using ArxisStudio.Markup.Metadata.Json;

var overlay = DesignOverlaySerializer.Deserialize(File.ReadAllText("MainView.design.json"));
Console.WriteLine($"Nodes: {overlay.Nodes.Count}");
```
