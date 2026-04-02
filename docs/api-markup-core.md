# ArxisStudio.Markup

Core-пакет с моделью `.arxui`.

## Ключевые типы

- `UiDocument`  
  `SchemaVersion`, `Kind`, `Class`, `Root`, `Design`.
- `UiNode`  
  `TypeName`, `Properties`, `Styles`, `Resources`, `Design`.
- `UiValue`  
  `ScalarValue`, `NodeValue`, `CollectionValue`, `BindingValue`, `ResourceValue`, `UriReferenceValue`.
- `UiDesignData` / `UiDesignValue`  
  `UiDesignScalarValue`, `UiDesignObjectValue`, `UiDesignCollectionValue`.

## Пример `.arxui` с inline `$design`

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "$design": {
    "SurfaceWidth": 1280
  },
  "Root": {
    "TypeName": "Avalonia.Controls.Canvas",
    "Properties": {
      "Children": [
        {
          "TypeName": "Avalonia.Controls.Border",
          "$design": {
            "Layout.X": 100,
            "Layout.Y": 200
          },
          "Properties": {}
        }
      ]
    }
  }
}
```
