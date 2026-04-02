# ArxisStudio.Markup.Metadata

Минимальный пакет metadata для inline `$design`.

## Основные типы

- `DesignMetadata`
- `NodeRef`
- `DocumentDesignMetadata`
- `NodeDesignMetadata`
- `DesignValue` (`DesignScalarValue`, `DesignObjectValue`, `DesignCollectionValue`)
- `IMetadataValidator`
- `SimpleMetadataValidator`
- `MetadataDiagnostic`
- `MetadataDiagnosticCodes`

## Что упрощено

1. Нет `KnownDesignProperties`.
2. Нет реестра/алиасов свойств.
3. В metadata разрешены любые полные ключи (`OwnerType.Property`).

## Inline API

`InlineDesignMetadataConverter`:

- `DesignMetadata FromInlineDesign(UiDocument document)`
- `UiDocument ApplyOverlay(UiDocument document, DesignMetadata metadata)`

## Пример

```csharp
var metadata = InlineDesignMetadataConverter.FromInlineDesign(document);
var diagnostics = new SimpleMetadataValidator().Validate(metadata);
var updatedDocument = InlineDesignMetadataConverter.ApplyOverlay(document, metadata);
```
