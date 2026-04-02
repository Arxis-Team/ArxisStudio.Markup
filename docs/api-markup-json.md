# ArxisStudio.Markup.Json

Сериализатор `.arxui`.

## Публичный API

- `UiDocument? ArxuiSerializer.Deserialize(string json)`
- `string ArxuiSerializer.Serialize(UiDocument document)`

## Что поддерживается

- runtime-секции (`Root`, `Properties`, `Styles`, `Resources`)
- спец-значения (`$binding`, `$resource`, `$asset`, `$styleInclude`, `$mergedDictionaries`)
- inline `$design` на уровне документа и узла

## Пример `.arxui` с `$design`

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "$design": {
    "GridVisible": true
  },
  "Root": {
    "TypeName": "Avalonia.Controls.Border",
    "$design": {
      "Layout.X": 120
    },
    "Properties": {
      "Width": 180
    }
  }
}
```
