# DesignEditor Bridge

`ArxisStudio.Markup.DesignEditorBridge` реализует bridge-слой между `DesignOverlay` и контролами в визуальном редакторе.

## Кратко

1. Runtime-структура UI живёт в `.arxui`.
2. Design-time свойства живут отдельно в `DesignOverlay`.
3. Bridge умеет валидировать metadata, применять её к контролам и извлекать обратно.

## Быстрый пример

```csharp
using ArxisStudio.Markup.DesignEditorBridge;

var runtime = DesignEditorBridgeRuntime.CreateDefault();

var metadataDiagnostics = runtime.Validate(overlay);
var bridgeDiagnostics = runtime.Apply(overlay, controlMap);
var extracted = runtime.Extract(controlMap);
```

## Для полной кастомизации

Используйте `DesignEditorBridgeRuntime.CreateEmpty()` и вручную регистрируйте:

- `DesignPropertyDescriptor` в `PropertyRegistry`
- `IDesignPropertyApplier` в `ApplierRegistry`
- `IDesignPropertyReader` в `ReaderRegistry`

## Детальная API-документация

- [API: ArxisStudio.Markup.DesignEditorBridge](./api-design-editor-bridge.md)
- [API: ArxisStudio.Markup.Metadata](./api-markup-metadata.md)
