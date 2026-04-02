# Inline `$design` Workflow

Документ описывает рекомендуемый цикл работы конструктора, если design-данные хранятся прямо в `.arxui`.

## Цель

Сделать `.arxui` единственным источником истины и не завязывать core-пакеты на редакторную инфраструктуру.

## Поток данных

1. Загрузить документ:
```csharp
var document = ArxuiSerializer.Deserialize(File.ReadAllText(path))!;
```
2. Преобразовать inline `$design` в runtime-редактируемую модель metadata:
```csharp
var metadata = InlineDesignMetadataConverter.FromInlineDesign(document);
```
3. Применить metadata в вашей редакторной логике:
```csharp
editor.Apply(metadata, controlMap);
```
4. После действий пользователя извлечь новые значения:
```csharp
var extracted = editor.Extract(controlMap);
```
5. Записать изменения обратно в `.arxui`:
```csharp
var updated = InlineDesignMetadataConverter.ApplyOverlay(document, extracted);
File.WriteAllText(path, ArxuiSerializer.Serialize(updated));
```

## Почему это архитектурно чисто

1. Runtime/serialization остаются в `ArxisStudio.Markup` и `ArxisStudio.Markup.Json`.
2. Редакторный слой можно заменить без изменения формата `.arxui`.
3. Конвертация сосредоточена в одном адаптере:
   `InlineDesignMetadataConverter`.

## Обработка диагностик

На каждом шаге проверяйте диагностики вашего редакторного слоя и результаты валидации metadata перед сохранением.


