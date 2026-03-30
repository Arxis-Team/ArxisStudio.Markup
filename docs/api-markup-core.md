# ArxisStudio.Markup

Пакет `ArxisStudio.Markup` содержит runtime-модель документа `.arxui`.

## Основные типы

### `UiDocument`

Корневая запись документа:

- `SchemaVersion: int` — версия схемы (текущий рабочий формат: `1`).
- `Kind: UiDocumentKind` — тип документа (`Application`, `Control`, `Window`, `Styles`, `ResourceDictionary`).
- `Class: string?` — полное имя CLR-типа (для генератора/связки с кодом).
- `Root: UiNode` — корень визуального дерева.

### `UiNode`

Узел дерева:

- `TypeName: string` — CLR-тип контрола/объекта.
- `Properties: IReadOnlyDictionary<string, UiValue>` — свойства узла.
- `Styles: UiStyles?` — локальные стили.
- `Resources: UiResources?` — локальные ресурсы.

### `UiValue` (иерархия)

1. `ScalarValue(object? Value)` — скаляр.
2. `NodeValue(UiNode Node)` — вложенный объект.
3. `CollectionValue(IReadOnlyList<UiValue> Items)` — коллекция.
4. `BindingValue(BindingSpec Binding)` — binding.
5. `ResourceValue(string Key)` — ссылка на ресурс.
6. `UriReferenceValue(string Path, string? Assembly = null)` — ссылка на asset/ресурс по пути.

### Binding API

- `BindingSpec`
- `BindingMode`
- `RelativeSourceSpec`
- `RelativeSourceMode`

## Пример: построение модели в коде

```csharp
using ArxisStudio.Markup;

var root = new UiNode(
    TypeName: "Avalonia.Controls.Grid",
    Properties: new Dictionary<string, UiValue>
    {
        ["Width"] = new ScalarValue(640),
        ["Height"] = new ScalarValue(360),
        ["Children"] = new CollectionValue(new UiValue[]
        {
            new NodeValue(new UiNode(
                "Avalonia.Controls.TextBlock",
                new Dictionary<string, UiValue>
                {
                    ["Text"] = new ScalarValue("Привет, ArxisStudio"),
                    ["HorizontalAlignment"] = new ScalarValue("Center"),
                    ["VerticalAlignment"] = new ScalarValue("Center")
                }))
        })
    });

var document = new UiDocument(
    SchemaVersion: 1,
    Kind: UiDocumentKind.Control,
    Class: "Demo.Views.MainView",
    Root: root);
```

## Пример: binding-значение

```csharp
var textNode = new UiNode(
    "Avalonia.Controls.TextBlock",
    new Dictionary<string, UiValue>
    {
        ["Text"] = new BindingValue(new BindingSpec(
            Path: "User.Name",
            Mode: BindingMode.OneWay,
            StringFormat: "Пользователь: {0}"))
    });
```

## Пример `.arxui` (полное runtime-дерево)

Файл `MainView.arxui`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "Demo.Views.MainView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.Grid",
        "Properties": {
          "Children": [
            {
              "TypeName": "Avalonia.Controls.TextBlock",
              "Properties": {
                "Text": {
                  "$binding": "User.Name",
                  "Mode": "OneWay",
                  "StringFormat": "Пользователь: {0}"
                }
              }
            }
          ]
        }
      }
    }
  }
}
```
