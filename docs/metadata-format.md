# Формат Metadata

Design-time данные хранятся в `$design` внутри `.arxui`.

## Канонический пример

```json
"$design": {
  "ArxisStudio.Attached.Layout.X": 100,
  "ArxisStudio.Attached.Layout.Y": 240,
  "Avalonia.Input.InputElement.IsHitTestVisible": false,
  "Avalonia.Controls.Design.Width": 800,
  "Avalonia.Controls.Design.Height": 600
}
```

## Правила формата

1. Используйте только полные ключи (`OwnerType.Property`).
2. Не используйте алиасы и сокращения.
3. `ArxisStudio.Markup.Metadata` не ограничивает список ключей.

## Валидация

`SimpleMetadataValidator` проверяет только структуру:

- непустой `NodeRef`;
- непустой ключ свойства.
