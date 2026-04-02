# ArxisStudio.Markup.Metadata.Json

Пакет сериализует `DesignMetadata` в JSON и обратно.

## Когда использовать

Основной сценарий конструктора — inline `$design` в `.arxui`.  
`DesignMetadataSerializer` нужен как вспомогательный формат:

1. экспорт/импорт metadata;
2. отладка bridge;
3. промежуточные инструменты миграции.

## Публичный API

- `DesignMetadata DesignMetadataSerializer.Deserialize(string json)`
- `string DesignMetadataSerializer.Serialize(DesignMetadata overlay)`

## Пример

```csharp
var overlay = InlineDesignMetadataConverter.FromInlineDesign(document);
var json = DesignMetadataSerializer.Serialize(overlay);

var restored = DesignMetadataSerializer.Deserialize(json);
var updatedDocument = InlineDesignMetadataConverter.ApplyOverlay(document, restored);
```


