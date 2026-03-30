# DesignEditor Bridge

`ArxisStudio.Markup.DesignEditorBridge` связывает `DesignOverlay` и `ArxisStudio.DesignEditor`.

## Основные компоненты

- `DesignEditorBridgeRuntime` — единая точка конфигурации и использования bridge.
- `DesignOverlayApplier` — применение метаданных к контролам.
- `DesignOverlayExtractor` — извлечение метаданных из контролов.
- `DesignPropertyRegistry` — разрешение канонических ключей и алиасов.
- `DesignPropertyApplierRegistry` — обработчики применения значений.
- `DesignPropertyReaderRegistry` — обработчики чтения значений.
- `DefaultDesignEditorMappings` — встроенные маппинги для известных ключей.

## Диагностики bridge-слоя

- `ADB0001` — для `NodeRef` не найден контрол.
- `ADB0002` — неизвестный ключ свойства дизайнера.
- `ADB0003` — не зарегистрирован обработчик применения.
- `ADB0004` — значение свойства не является скаляром.

## Режимы инициализации

- `DesignEditorBridgeRuntime.CreateDefault()`:
  регистрирует встроенные свойства и маппинги.
- `DesignEditorBridgeRuntime.CreateEmpty()`:
  создаёт runtime без встроенных регистраций (для полностью кастомной конфигурации).

## Типовой сценарий

```csharp
var runtime = DesignEditorBridgeRuntime.CreateDefault();

var applyDiagnostics = runtime.Apply(overlay, controlMap);
var extractedOverlay = runtime.Extract(controlMap);
var metadataDiagnostics = runtime.Validate(overlay);
```
