# ArxisStudio.Markup.Metadata

Пакет хранит design-time данные отдельно от runtime `.arxui`.

## Модель данных

### `DesignOverlay`

- `Document: DocumentDesignData?` — свойства уровня документа.
- `Nodes: IReadOnlyDictionary<NodeRef, NodeDesignData>` — свойства узлов.

### `NodeRef`

- `NodeRef(string Value)` — стабильная ссылка на узел внутри документа.

### `DocumentDesignData` и `NodeDesignData`

- `Properties: IReadOnlyDictionary<string, DesignValue>`

### `DesignValue` (иерархия)

1. `DesignScalarValue(object? Value)`
2. `DesignObjectValue(IReadOnlyDictionary<string, DesignValue> Properties)`
3. `DesignCollectionValue(IReadOnlyList<DesignValue> Items)`

## Контракты и валидация

### `DesignPropertyDescriptor`

- `CanonicalKey: string`
- `ValueType: Type`
- `Aliases: IReadOnlyCollection<string>?`

### `IDesignPropertyRegistry`

- `TryGetByCanonicalKey(...)`
- `TryResolve(...)`
- `GetAll()`

### `IMetadataValidator`

- `Validate(DesignOverlay overlay, IDesignPropertyRegistry registry)`

### `MetadataDiagnostic`

- `Code`
- `Message`
- `NodeRef`
- `PropertyKey`

## Встроенные константы

### `KnownDesignProperties`

- `Avalonia.Input.InputElement.IsHitTestVisible` (`IsHitTestVisible`)
- `ArxisStudio.Attached.Layout.X` (`Layout.X`)
- `ArxisStudio.Attached.Layout.Y` (`Layout.Y`)
- `ArxisStudio.Attached.DesignInteraction.MovePolicy` (`DesignInteraction.MovePolicy`)
- `ArxisStudio.Attached.DesignInteraction.ResizePolicy` (`DesignInteraction.ResizePolicy`)

Также предоставляет `Descriptors` — встроенный набор дескрипторов.

### `MetadataDiagnosticCodes`

- `MDV0001` — пустой `NodeRef`.
- `MDV0002` — свойство не зарегистрировано.
- `MDV0003` — значение свойства не скаляр.
- `MDV0004` — тип значения несовместим с `ValueType`.

## `SimpleMetadataValidator`

Базовая реализация `IMetadataValidator`, проверяет:

1. Валидность `NodeRef`.
2. Наличие свойства в registry (включая алиасы).
3. Скалярность значения.
4. Совместимость типа значения с ожидаемым `ValueType`.

## Пример: создание overlay

```csharp
using ArxisStudio.Markup.Metadata;

var overlay = new DesignOverlay(
    Document: new DocumentDesignData(new Dictionary<string, DesignValue>
    {
        ["SurfaceWidth"] = new DesignScalarValue(1280),
        ["SurfaceHeight"] = new DesignScalarValue(720)
    }),
    Nodes: new Dictionary<NodeRef, NodeDesignData>
    {
        [new NodeRef("/Root/Children/0")] = new NodeDesignData(
            new Dictionary<string, DesignValue>
            {
                ["IsHitTestVisible"] = new DesignScalarValue(false),
                ["Layout.X"] = new DesignScalarValue(100),
                ["Layout.Y"] = new DesignScalarValue(200)
            })
    });
```
