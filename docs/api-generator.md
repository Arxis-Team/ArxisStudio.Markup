# ArxisStudio.Markup.Generator

Пакет содержит Roslyn incremental generator, который обрабатывает `.arxui` из `AdditionalFiles` и генерирует `InitializeComponent()`.

## Публичный API

### `ArxuiGenerator : IIncrementalGenerator`

- `Initialize(IncrementalGeneratorInitializationContext context)`  
  Регистрирует pipeline:
  1. Фильтрация `AdditionalText` по расширению `.arxui`.
  2. Десериализация через `ArxuiSerializer`.
  3. Проверки согласованности документа/типа.
  4. Генерация `*.g.cs`.

### `DiagnosticDescriptors`

Публичные дескрипторы диагностик:

- `ADG0001` `JsonParseError`
- `ADG0002` `TypeNotFound`
- `ADG0003` `PropertyNotFound`
- `ADG0004` `EventNotFound`
- `ADG0005` `TargetClassMissing`
- `ADG0006` `TargetClassNotFound`
- `ADG0007` `TargetClassMustBePartial`
- `ADG0008` `DocumentKindMismatch`
- `ADG0009` `DuplicateTargetClass`
- `ADG0010` `RootTypeKindMismatch`
- `ADG0011` `RootTypeTargetClassMismatch`
- `ADG9999` `GeneratorCrash`

## Подключение в проект

```xml
<ItemGroup>
  <ProjectReference Include="..\ArxisStudio.Markup.Generator\ArxisStudio.Markup.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<ItemGroup>
  <AdditionalFiles Include="Views\**\*.arxui" />
</ItemGroup>
```

## Требования к целевому типу

1. В `.arxui` должен быть `Class` для `Application`/`Control`/`Window`.
2. Указанный тип должен существовать в компиляции.
3. Класс должен быть `partial`.
4. `UiDocumentKind`, тип `Root` и целевой CLR-тип должны быть совместимы.

## Пример файла `.arxui`

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "Demo.Views.MainView",
  "Root": {
    "TypeName": "Demo.Views.MainView",
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.TextBlock",
        "Properties": {
          "Text": "Generated view"
        }
      }
    }
  }
}
```

## Примеры `.arxui` по вариантам `UiDocumentKind`

`Application`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Application",
  "Class": "Demo.App",
  "Root": {
    "TypeName": "Demo.App",
    "Properties": {}
  }
}
```

`Window`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Window",
  "Class": "Demo.Views.MainWindow",
  "Root": {
    "TypeName": "Demo.Views.MainWindow",
    "Properties": {
      "Title": "Main Window"
    }
  }
}
```
