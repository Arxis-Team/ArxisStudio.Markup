# Формат Metadata

`ArxisStudio.Markup.Metadata` хранит design-time данные отдельно от runtime-модели.

## Базовые правила

- Runtime UI-модель задаётся только в `ArxisStudio.Markup`.
- Данные дизайнера представлены типом `DesignOverlay`.
- Свойства метаданных определяются через реестр (`IDesignPropertyRegistry`).
- `SchemaVersion` в `.arxui` остаётся равным `1`.

## Разделение ответственности

- `.arxui` (`UiDocument`) содержит только runtime-структуру.
- Метаданные дизайнера загружаются и сохраняются отдельно как `DesignOverlay`.

## Структура оверлея

```json
{
  "Document": {
    "SurfaceWidth": 1280,
    "SurfaceHeight": 720
  },
  "Nodes": {
    "/Root/Children/0": {
      "IsHitTestVisible": false,
      "Layout.X": 100,
      "Layout.Y": 200,
      "DesignInteraction.MovePolicy": "X",
      "DesignInteraction.ResizePolicy": "None"
    }
  }
}
```

## Встроенные ключи

- `Avalonia.Input.InputElement.IsHitTestVisible` (алиас: `IsHitTestVisible`)
- `ArxisStudio.Attached.Layout.X` (алиас: `Layout.X`)
- `ArxisStudio.Attached.Layout.Y` (алиас: `Layout.Y`)
- `ArxisStudio.Attached.DesignInteraction.MovePolicy` (алиас: `DesignInteraction.MovePolicy`)
- `ArxisStudio.Attached.DesignInteraction.ResizePolicy` (алиас: `DesignInteraction.ResizePolicy`)

Ключи объявлены в `KnownDesignProperties`.

## Валидация

`SimpleMetadataValidator` возвращает диагностики:

- `MDV0001` — пустой `NodeRef`
- `MDV0002` — неизвестное свойство
- `MDV0003` — не-скалярное значение свойства
- `MDV0004` — несовместимый тип значения
