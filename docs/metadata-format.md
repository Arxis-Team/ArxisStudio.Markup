# Формат Metadata

`ArxisStudio.Markup.Metadata` задаёт формат design-time данных, который хранится отдельно от runtime `.arxui`.

## Структура

```json
{
  "Document": {
    "SurfaceWidth": 1280,
    "SurfaceHeight": 720
  },
  "Nodes": {
    "/Root/Children/0": {
      "Avalonia.Input.InputElement.IsHitTestVisible": false,
      "ArxisStudio.Attached.Layout.X": 100,
      "ArxisStudio.Attached.Layout.Y": 200
    }
  }
}
```

## Модель данных

- `DesignOverlay` — корень metadata.
- `DocumentDesignData` — свойства уровня документа.
- `NodeDesignData` — свойства уровня узла.
- `DesignValue` — скаляр/объект/коллекция.
- `NodeRef` — ссылка на узел в дереве документа.

## Правила

1. Runtime API (`UiDocument`, `UiNode`) не содержит `$design`.
2. Design-time свойства определяются через registry (`IDesignPropertyRegistry`).
3. Для интеграции с редактором используется отдельный bridge-пакет.

## Валидация

`SimpleMetadataValidator` возвращает диагностические коды:

- `MDV0001` пустой `NodeRef`
- `MDV0002` неизвестное свойство
- `MDV0003` не-скалярное значение
- `MDV0004` несовместимый тип значения

## Детальная API-документация

- [API: ArxisStudio.Markup.Metadata](./api-markup-metadata.md)
- [API: ArxisStudio.Markup.Metadata.Json](./api-markup-metadata-json.md)
