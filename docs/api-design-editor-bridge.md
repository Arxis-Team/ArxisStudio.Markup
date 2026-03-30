# ArxisStudio.Markup.DesignEditorBridge

Пакет связывает `DesignOverlay` и реальные `Avalonia.Controls.Control`.

## Основной фасад

### `DesignEditorBridgeRuntime`

- `CreateEmpty(IMetadataValidator? validator = null)` — пустой runtime без встроенных регистраций.
- `CreateDefault(IMetadataValidator? validator = null)` — runtime с `KnownDesignProperties` и встроенными маппингами.
- `Validate(DesignOverlay overlay)` — валидация metadata через `IMetadataValidator`.
- `Apply(DesignOverlay overlay, IReadOnlyDictionary<NodeRef, Control> controlMap)` — применение metadata к контролам.
- `Extract(IReadOnlyDictionary<NodeRef, Control> controlMap, DocumentDesignData? document = null)` — извлечение metadata из контролов.

## Компоненты

- `DesignOverlayApplier` — применяет `DesignScalarValue` к контролам.
- `DesignOverlayExtractor` — читает значения из контролов и строит overlay.
- `DesignPropertyRegistry` — registry canonical ключей и алиасов.
- `DesignPropertyApplierRegistry` — registry применителей (`IDesignPropertyApplier`).
- `DesignPropertyReaderRegistry` — registry читателей (`IDesignPropertyReader`).

## Расширяемые интерфейсы

- `IDesignPropertyApplierRegistry`
- `IDesignPropertyApplier`
- `IDesignPropertyReaderRegistry`
- `IDesignPropertyReader`

## Диагностики bridge-слоя

- `ADB0001` (`ControlNotFound`) — control отсутствует для `NodeRef`.
- `ADB0002` (`UnknownDesignProperty`) — ключ свойства неизвестен registry.
- `ADB0003` (`ApplierNotRegistered`) — нет `IDesignPropertyApplier` для canonical ключа.
- `ADB0004` (`NonScalarValue`) — значение свойства не `DesignScalarValue`.

## Встроенные маппинги

`DefaultDesignEditorMappings` предоставляет extension-методы:

- `RegisterKnownProperties(this DesignPropertyRegistry registry)`
- `RegisterKnownAppliers(this DesignPropertyApplierRegistry registry)`
- `RegisterKnownReaders(this DesignPropertyReaderRegistry registry)`

## Пример: стандартный runtime

```csharp
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;

var runtime = DesignEditorBridgeRuntime.CreateDefault();

IReadOnlyList<MetadataDiagnostic> metadataDiagnostics = runtime.Validate(overlay);
IReadOnlyList<BridgeDiagnostic> applyDiagnostics = runtime.Apply(overlay, controlMap);
DesignOverlay roundTripOverlay = runtime.Extract(controlMap);
```

## Пример: кастомная регистрация свойства

```csharp
using ArxisStudio.Markup.DesignEditorBridge;
using ArxisStudio.Markup.Metadata;

var runtime = DesignEditorBridgeRuntime.CreateEmpty();

runtime.PropertyRegistry.Register(new DesignPropertyDescriptor(
    canonicalKey: "MyCompany.Design.GridSnap",
    valueType: typeof(bool),
    aliases: new[] { "GridSnap" }));

runtime.ApplierRegistry.Register(
    "MyCompany.Design.GridSnap",
    new GridSnapApplier());

runtime.ReaderRegistry.Register(
    "MyCompany.Design.GridSnap",
    new GridSnapReader());
```
