# Миграция: `$design` -> `DesignOverlay`

Документ описывает переход на отдельный слой metadata без встроенных `$design`-полей в `.arxui`.

## Что изменилось

1. `$design` удалён из runtime-модели `UiDocument`/`UiNode`.
2. `ArxuiSerializer` больше не читает и не пишет `$design` внутри `.arxui`.
3. Design-time данные теперь хранятся как отдельный `DesignOverlay`.

## Новый поток данных

1. Загрузить `.arxui` через `ArxuiSerializer.Deserialize(...)`.
2. Загрузить `DesignOverlay` отдельно через `DesignOverlaySerializer.Deserialize(...)`.
3. В редакторе использовать `DesignEditorBridgeRuntime`:
   `Validate(...)`, `Apply(...)`, `Extract(...)`.

## Пример

```csharp
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Metadata.Json;
using ArxisStudio.Markup.DesignEditorBridge;

var ui = ArxuiSerializer.Deserialize(File.ReadAllText("MainView.arxui"));
var overlay = DesignOverlaySerializer.Deserialize(File.ReadAllText("MainView.design.json"));

var runtime = DesignEditorBridgeRuntime.CreateDefault();
var validation = runtime.Validate(overlay);
var applyDiagnostics = runtime.Apply(overlay, controlMap);
```

## Что делать со старыми файлами

1. Перенести все `$design`-данные из `.arxui` в отдельный JSON overlay-файл.
2. Привести ключи к canonical-форме или зарегистрированным алиасам.
3. Проверить overlay через `runtime.Validate(...)`.

## Детальная API-документация

- [Индекс API-документации](./api-index.md)
