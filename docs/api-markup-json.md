# ArxisStudio.Markup.Json

Пакет отвечает за JSON-представление `.arxui`.

## Публичный API

### `ArxuiSerializer`

- `UiDocument? Deserialize(string json)`  
  Десериализует JSON в `UiDocument`. Возвращает `null`, если отсутствует корневой узел.
- `string Serialize(UiDocument document)`  
  Сериализует `UiDocument` в JSON.

## Поддерживаемые специальные JSON-конструкции

- Binding: `"$binding"`
- Resource reference: `"$resource"`
- Asset reference: `"$asset"`
- Include style: `"$styleInclude"`
- Merged dictionaries: `"$mergedDictionaries"`

## Пример: чтение документа

```csharp
using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;

var json = File.ReadAllText("MainView.arxui");
UiDocument? document = ArxuiSerializer.Deserialize(json);

if (document is null)
{
    throw new InvalidOperationException("Документ не содержит Root.");
}

Console.WriteLine($"Kind = {document.Kind}, Class = {document.Class}");
```

## Пример: запись документа

```csharp
using ArxisStudio.Markup.Json;

string output = ArxuiSerializer.Serialize(document);
File.WriteAllText("MainView.generated.arxui", output);
```

## Минимальный JSON-пример

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "Demo.Views.MainView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.TextBlock",
        "Properties": {
          "Text": "Hello"
        }
      }
    }
  }
}
```

## Пример `.arxui` со спец-конструкциями API

Файл `Dashboard.arxui`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "Demo.Views.DashboardView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "Resources": {
      "$mergedDictionaries": [
        { "Source": "avares://Demo/Styles/Theme.axaml" }
      ],
      "AccentBrush": "#2E8B57"
    },
    "Styles": [
      { "$styleInclude": "avares://Demo/Styles/Dashboard.axaml" }
    ],
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.StackPanel",
        "Properties": {
          "Children": [
            {
              "TypeName": "Avalonia.Controls.TextBlock",
              "Properties": {
                "Text": { "$binding": "Header.Title", "Mode": "OneWay" },
                "Foreground": { "$resource": "AccentBrush" }
              }
            },
            {
              "TypeName": "Avalonia.Controls.Image",
              "Properties": {
                "Source": { "$asset": "Assets/logo.png" }
              }
            }
          ]
        }
      }
    }
  }
}
```
