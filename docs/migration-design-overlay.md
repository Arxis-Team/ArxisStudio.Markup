# Миграция: `DesignMetadata` -> inline `$design`

Документ описывает переход к хранению design-time данных внутри `.arxui`.

## Что изменилось

1. В `UiDocument` и `UiNode` добавлено поле `Design` (`$design` в JSON).
2. `ArxuiSerializer` читает и пишет `$design` на уровне документа и узла.
3. Для editor-логики используется `DesignMetadata` + `InlineDesignMetadataConverter`.

## Рекомендуемый поток для конструктора

1. Загрузить `UiDocument` из `.arxui`.
2. Получить metadata:
   `InlineDesignMetadataConverter.FromInlineDesign(document)`.
3. Применить metadata в редакторной логике.
4. Извлечь обновленную metadata после редактирования.
5. Записать обратно в `.arxui`:
   `InlineDesignMetadataConverter.ApplyOverlay(document, extractedMetadata)`.

## Минимальный пример

```csharp
var document = ArxuiSerializer.Deserialize(File.ReadAllText("MainView.arxui"))!;
var metadata = InlineDesignMetadataConverter.FromInlineDesign(document);

editor.Apply(metadata, controlMap);
var extractedMetadata = editor.Extract(controlMap);

var updated = InlineDesignMetadataConverter.ApplyOverlay(document, extractedMetadata);
File.WriteAllText("MainView.arxui", ArxuiSerializer.Serialize(updated));
```
