# Миграция: `$design` -> `DesignOverlay`

## Что изменилось

- Поля `$design` удалены из `UiDocument` и `UiNode`.
- `ArxuiSerializer` больше не читает и не пишет `$design` внутри `.arxui`.
- Design-time данные представлены отдельным типом:
  `ArxisStudio.Markup.Metadata.DesignOverlay`.

## Новый поток данных

1. Загрузить `.arxui` через `ArxuiSerializer` и получить чистый runtime `UiDocument`.
2. Загрузить метаданные дизайнера отдельно как `DesignOverlay`
   (например, через `DesignOverlaySerializer`).
3. Использовать `DesignEditorBridgeRuntime`:
   - `Validate` — проверка метаданных;
   - `Apply` — применение к контролам;
   - `Extract` — извлечение из контролов.

## Минимальная инициализация

```csharp
var runtime = DesignEditorBridgeRuntime.CreateDefault();
var diagnostics = runtime.Apply(overlay, controlMap);
```
