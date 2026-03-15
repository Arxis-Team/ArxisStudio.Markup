# ArxisStudio.Markup

`ArxisStudio.Markup` — инфраструктура для model-driven описания интерфейсов Avalonia на основе JSON-документов `.arxui`.

Текущее ядро разделено на четыре библиотеки:

- `ArxisStudio.Markup`
- `ArxisStudio.Markup.Json`
- `ArxisStudio.Markup.Json.Loader`
- `ArxisStudio.Markup.Generator`

Библиотека не пытается заменить Avalonia XAML как язык ручной разработки. Её задача — дать структурированную модель, пригодную для визуального конструктора, трансформаций дерева, сериализации и code generation.

## Назначение

Обычный XAML хорошо подходит для ручной разработки представлений, но плохо подходит как внутренний формат визуального редактора. Для конструктора нужна не текстовая разметка, а объектная модель документа с предсказуемой сериализацией.

`.arxui` используется именно в этой роли:

- хранит дерево UI и значения свойств в структурированном виде;
- служит входом для source generator;
- служит форматом сохранения для редактора;
- позволяет выполнять round-trip сериализацию без потери структуры.

Ключевой архитектурный принцип проекта: библиотека не должна зависеть от заранее заданного каталога контролов. Она работает с произвольными типами из проекта и зависимостей, разрешая их по CLR-именам.

## Основные свойства

- нет жёстко зашитого списка поддерживаемых контролов;
- нет необходимости добавлять специальную поддержку для каждого пользовательского контрола;
- generator разрешает типы и свойства через Roslyn;
- editor строит preview по той же модели, но без выполнения пользовательского runtime-кода;
- стили и ресурсы поддерживаются в гибридной схеме: layout хранится в `.arxui`, сложные styles/resources могут оставаться в `.axaml`.

## Состав репозитория

- `ArxisStudio.Markup`
  Нейтральная модель документа `.arxui`: `UiDocument`, `UiNode`, `UiValue`, `$design`.

- `ArxisStudio.Markup.Json`
  JSON serializer/deserializer формата `.arxui`.

- `ArxisStudio.Markup.Json.Loader`
  Runtime/design-time loader, строящий Avalonia object tree из `.arxui`.

- `ArxisStudio.Markup.Generator`
  Инкрементальный Roslyn source generator `ArxisStudio.Markup.Generator`.

- `ArxisStudio.Editor`
  Визуальный редактор и design-time previewer.

- `ArxisStudio.Sample`
  Пример проекта, использующего generator.

- `ArxisStudio.Template`
  Шаблонный Avalonia-проект для открытия в `ArxisStudio.Editor`.

- `ArxisStudio.Tests`
  Тесты сериализации, диагностик и генерации исходного кода.

## Поддерживаемые типы документов

Поле `Kind` определяет семантический тип документа. Сейчас поддерживаются:

- `Application`
- `Control`
- `Window`
- `Styles`
- `ResourceDictionary`

Конкретный CLR root type задаётся через `Root.TypeName`.

## Модель `.arxui`

Текущий контракт поддерживает:

- создание объектов по CLR-имени типа;
- вложенные узлы;
- scalar properties;
- свойства-коллекции, например `Children`;
- attached properties, например `Grid.Row` и `Canvas.Left`;
- ресурсы через `$resource`;
- bindings через `$binding`;
- assets через `$asset`;
- host-level `Styles`;
- host-level `Resources`;
- `StyleInclude`;
- merged resource dictionaries;
- keyed resources;
- design-time metadata через `$design`.

### Design-time metadata

`$design` — служебный канал для редактора, аналогичный по смыслу `d:*` в XAML. Эти данные не должны влиять на production runtime и не должны использоваться generator как часть боевой семантики документа.

На текущем этапе поддерживаются:

- document-level:
  - `SurfaceWidth`
  - `SurfaceHeight`

- node-level:
  - `Locked`
  - `Hidden`
  - `IgnorePreviewInput`
  - `AllowMove`
  - `AllowResize`

Пример:

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "$design": {
    "SurfaceWidth": 1440,
    "SurfaceHeight": 900
  },
  "Class": "MyApp.Views.DashboardView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "$design": {
      "IgnorePreviewInput": true,
      "AllowMove": true,
      "AllowResize": true
    },
    "Properties": {}
  }
}
```

## Генерация кода

Обычный сценарий использования generator:

1. В проекте объявляется `partial`-класс Avalonia-представления.
2. Создаётся соответствующий `.arxui`.
3. В `Class` указывается полное CLR-имя целевого типа.
4. `.arxui` передаётся в проект как `AdditionalFiles`.
5. Generator валидирует документ и генерирует `InitializeComponent()`.

Generator проверяет:

- корректность JSON-документа;
- наличие root type;
- совместимость `Kind` с root type;
- наличие `Class` там, где он обязателен;
- `partial`-модификатор target type;
- совместимость target type и root type;
- конфликты между несколькими `.arxui`, указывающими на один CLR type.

### Пример

Исходный класс:

```csharp
public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
    }
}
```

Соответствующий `.arxui`:

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "MyApp.Views.ProfileView",
  "Root": {
    "TypeName": "Avalonia.Controls.UserControl",
    "Properties": {
      "Content": {
        "TypeName": "Avalonia.Controls.Border",
        "Properties": {
          "Padding": 16,
          "BorderThickness": 1,
          "BorderBrush": "Gray"
        }
      }
    }
  }
}
```

На этапе сборки generator создаёт C#-код, который строит дерево объектов и присваивает свойства внутри `InitializeComponent()`.

## `Application`-документы

Поддерживаются документы уровня `Application`. Это позволяет описывать `App` через `.arxui`, а не через `App.axaml`, если такой режим подходит проекту.

В шаблонном проекте этот сценарий показан на:

- [App.arxui](/home/deck/Desktop/SourceGenerationExample/ArxisStudio.Template/Metadata/App.arxui)
- [App.arxui.cs](/home/deck/Desktop/SourceGenerationExample/ArxisStudio.Template/App.arxui.cs)

Это покрывает:

- application-level theme setup;
- application-level styles;
- application-level merged dictionaries.

## Стили и ресурсы

Рекомендуемая схема использования — гибридная.

`.arxui` должен описывать:

- дерево UI;
- composition;
- ресурсы узлов;
- ссылки на внешние style/resource файлы;
- bindings и assets.

`.axaml` остаётся подходящим местом для:

- глобальных стилей;
- сложных селекторов;
- крупных resource dictionaries;
- шаблонов и другой человекочитаемой XAML-разметки.

Такая схема позволяет не дублировать язык Avalonia styling в JSON, но при этом сохранять его видимым для инструментов через `StyleInclude` и merged dictionaries.

## `ArxisStudio.Editor`

`ArxisStudio.Editor` — design-time инструмент, а не runner пользовательского приложения.

Базовые правила editor:

- проект используется как источник типов, ресурсов, `.arxui` и `.axaml`;
- preview строится через `ArxisStudio.Markup.Json.Loader`;
- пользовательский code-behind и event handlers не должны исполняться;
- design-time chrome должен жить отдельно от production visual tree.

На текущем этапе editor умеет:

- открывать `.csproj` и `.sln`;
- индексировать `.arxui` и `.axaml` внутри проекта;
- открывать `.arxui` из дерева проекта;
- записывать изменения обратно в документ;
- строить preview из `.arxui`;
- применять локальные styles/resources;
- загружать внешние `.axaml` для preview;
- использовать document-level `$design.SurfaceWidth/SurfaceHeight`;
- учитывать node-level `$design.Hidden` и `$design.IgnorePreviewInput`.

Ограничения текущей реализации:

- preview ещё не эквивалентен runtime поведению приложения;
- загрузка project assemblies требует дальнейшего развития;
- bindings и сложные сценарии styling поддерживаются частично;
- selection/adorner layer пока не реализован полностью.

## Текущая архитектурная модель editor

Целевой конструктор должен строиться вокруг следующих принципов:

- `.arxui` — внутренний persistence format, а не пользовательский authoring format;
- production visual tree и design-time overlay должны быть разделены;
- выбор, рамки, handles и drag/resize должны рисоваться отдельным adorner-слоем;
- поведение перемещения и resize зависит от контейнера, а не только от самого узла;
- design metadata через `$design` должна оставаться ограниченной и не превращаться в общий state editor session.

## Шаблонный проект

`ArxisStudio.Template` используется как reference-проект для editor.

Он демонстрирует:

- `Application` в формате `.arxui`;
- host-level `Styles` и `Resources`;
- внешние `.axaml` styles/resources;
- использование `$design` в живом документе;
- загрузку проекта и preview внутри editor.

## Ограничения проекта

Проект намеренно не является полной реализацией Avalonia XAML.

Сейчас отсутствуют или поддерживаются частично:

- полное покрытие XAML markup extensions;
- полная модель templates/styles/animations;
- authoring-модель с namespace/prefix как в XAML;
- полное runtime parity для editor preview;
- полноценный visual designer с selection overlay, drag/drop и handles.

Это ограничение осознанное: библиотека разрабатывается как основа универсального конструктора, а не как альтернативный XAML-компилятор общего назначения.

## Сборка и запуск

Требование:

- .NET SDK 9.0

Основные команды:

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Markup.sln
dotnet run --project ArxisStudio.Sample/ArxisStudio.Markup.Sample.csproj
dotnet run --project ArxisStudio.Editor/ArxisStudio.Editor.csproj
dotnet run --project ArxisStudio.Template/ArxisStudio.Markup.Template.csproj
```

## Текущее состояние

На текущий момент:

- solution собирается;
- generator tests проходят;
- build-time generation поддерживает `Application`, `Control`, `Window`, `Styles`, `ResourceDictionary`;
- editor умеет работать с проектным контекстом и строить частичный style/resource-aware preview;
- контракт `.arxui` уже содержит минимальный design-time канал `$design`.

## Ближайшие шаги

Наиболее важные направления развития:

1. Развить project-context и assembly-based type resolution в editor.
2. Добавить отдельный selection/adorner layer поверх preview.
3. Реализовать container-specific designer behavior для `Canvas`, `Grid`, `StackPanel` и других контейнеров.
4. Расширить preview parity для ресурсов, стилей, шаблонов и bindings.
5. Построить полноценный visual designer вокруг той же модели `.arxui`, не смешивая design-time инфраструктуру с production деревом.
