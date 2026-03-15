# ArxisStudio.Markup

`ArxisStudio.Markup` это model-driven pipeline для UI на Avalonia.

Вместо того чтобы описывать визуальную структуру напрямую в XAML, интерфейс хранится в структурированных `.arxui` документах, а затем на этапе сборки превращается в обычный C# через Roslyn source generator. Цель проекта не в том, чтобы заменить Avalonia XAML один в один. Цель в том, чтобы дать формат, который предсказуем для инструментов, удобен для трансформаций и достаточно универсален, чтобы работать с неизвестными контролами, пользовательскими контролами и проектными типами без хардкода в библиотеке.

## Что Это Такое

Базовая идея проста:

- XAML удобен как формат ручного написания представлений.
- Визуальному конструктору нужна структурированная модель, а не текстовый язык.
- `.arxui` и есть такой persistence format.
- Source generator компилирует эту модель в `InitializeComponent()`.
- Редактор может инспектировать и изменять ту же самую модель, не переписывая текст разметки.

Из-за этого проект подходит для:

- визуальных конструкторов форм и экранов
- инспектора свойств
- drag-and-drop компоновки
- трансформаций модели
- round-trip сериализации
- build-time генерации Avalonia UI кода

## Главное Свойство

Самое важное архитектурное свойство библиотеки в том, что ей не нужно встроенное знание о фиксированном каталоге контролов.

Она работает от метаданных типов:

- `.arxui` хранит CLR-имена типов и структурированные значения свойств
- generator разрешает типы и члены через Roslyn
- editor разрешает и preview'ит модель в runtime

Это означает, что библиотека может работать с:

- стандартными контролами Avalonia
- пользовательскими контролами из проекта
- контролами из внешних библиотек
- проектными ресурсами и style-файлами

без необходимости добавлять отдельную поддержку под каждый новый тип.

## Структура Репозитория

- `ArxisStudio.Contracts`
  Контракты и JSON-сериализация модели `.arxui`.

- `ArxisStudio.Generator/ArxisStudio.Generator`
  Инкрементальный Roslyn source generator, который читает `.arxui` из `AdditionalFiles`, валидирует документы, разрешает типы и свойства и генерирует `InitializeComponent()`.

- `ArxisStudio.Editor`
  Экспериментальный визуальный редактор и previewer. Он редактирует `.arxui`, загружает контекст проекта и строит design-time preview без выполнения пользовательского event code.

- `ArxisStudio.Sample`
  Пример Avalonia-приложения, использующего generator.

- `ArxisStudio.Template`
  Шаблонный Avalonia-проект, предназначенный для открытия в `ArxisStudio.Editor`.

- `ArxisStudio.Tests`
  Тесты generator и serializer.

## Поддерживаемые Виды Документов

Сейчас модель поддерживает:

- `Application`
- `Control`
- `Window`
- `Styles`
- `ResourceDictionary`

`Kind` задаёт семантическую категорию документа. Конкретный CLR root type по-прежнему задаётся через `Root.TypeName`.

## Возможности Модели `.arxui`

Текущая модель умеет:

- создавать объекты по CLR-имени типа
- описывать вложенные графы объектов
- присваивать scalar properties
- работать со свойствами-коллекциями, например `Children`
- задавать attached properties, например `Grid.Row` или `Canvas.Left`
- использовать ресурсы через `$resource`
- использовать bindings через `$binding`
- загружать ассеты через `$asset`
- задавать host-level `Styles` и `Resources`
- подключать `StyleInclude`
- подключать merged resource dictionaries
- хранить локальные keyed resources у root и вложенных узлов
- хранить design-time metadata через `$design`

Этого уже достаточно для:

- `Application`-уровневого styling
- `Window` и `UserControl` деревьев
- локальных ресурсов внутри визуального дерева
- гибридного сценария, где стили живут в `.axaml`, а layout в `.arxui`

## Design-Time Метаданные

`.arxui` теперь поддерживает минимальный design-time канал `$design`, аналогичный по смыслу `d:*` в XAML.

Generator и production runtime должны игнорировать эти поля. Они предназначены для визуального редактора и preview.

На текущем этапе поддерживаются:

- document-level `SurfaceWidth`
- document-level `SurfaceHeight`
- node-level `Locked`
- node-level `Hidden`
- node-level `IgnorePreviewInput`
- node-level `AllowMove`
- node-level `AllowResize`

Пример:

```json
{
  "SchemaVersion": 1,
  "Kind": "Control",
  "Class": "MyApp.Views.DashboardView",
  "$design": {
    "SurfaceWidth": 1440,
    "SurfaceHeight": 900
  },
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

## Чем Проект Не Является

Это не полная реализация XAML.

Проект намеренно не пытается повторить весь язык Avalonia XAML:

- нет полной системы markup extensions
- нет полного покрытия template/style/animation
- нет полноценной authoring-модели с XAML namespace/prefix
- нет цели стать drop-in replacement для Avalonia XAML compiler

Этот компромисс сделан осознанно. Модель проектируется в первую очередь для инструментов.

## Как Работает Build-Time Генерация

Обычный workflow такой:

1. Объявляется обычный Avalonia `partial` class.
2. Создаётся соответствующий `.arxui` файл.
3. В `Class` указывается полное CLR-имя целевого типа.
4. `.arxui` добавляется в consuming project как `AdditionalFiles`.
5. Generator валидирует документ и генерирует `InitializeComponent()`.

Generator проверяет:

- парсится ли документ
- существует ли root type
- совместим ли `Kind` с root type
- существует ли `Class`, когда он обязателен
- является ли target type `partial`
- совместим ли target type с root type документа
- нет ли нескольких assets, указывающих на один и тот же CLR type

## Пример

`partial` class:

```csharp
public partial class SolidColorBrush : ContentControl
{
    public SolidColorBrush()
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
  "Class": "MyApp.Views.SolidColorBrush",
  "Root": {
    "TypeName": "Avalonia.Controls.ContentControl",
    "Properties": {
      "Width": 400,
      "Height": 250,
      "Content": {
        "TypeName": "Avalonia.Controls.Border",
        "Properties": {
          "Name": "NamedBorder",
          "BorderThickness": "3",
          "BorderBrush": "Gray"
        }
      }
    }
  }
}
```

Во время сборки generator превращает это описание в C#-код, который создаёт дерево контролов и присваивает свойства внутри `InitializeComponent()`.

## Документы Уровня Application

Библиотека теперь поддерживает и `Application`-документы.

Это означает, что в поддерживаемых сценариях `App.axaml` можно заменить на `.arxui`. Шаблонный проект показывает это на практике:

- [App.arxui](/home/deck/Desktop/SourceGenerationExample/ArxisStudio.Template/Metadata/App.arxui)
- [App.arxui.cs](/home/deck/Desktop/SourceGenerationExample/ArxisStudio.Template/App.arxui.cs)

Это позволяет моделировать через `.arxui`:

- application-level theme setup
- application-level styles
- application-level merged dictionaries

используя тот же persistence format, что и для остальных частей конструктора.

## Гибридная Стратегия Для Стилей

Рекомендуемая стратегия для styling здесь гибридная:

- `.arxui` владеет структурной UI-моделью
- `.axaml` остаётся хорошим местом для человекочитаемых стилей и resource dictionaries
- `.arxui` ссылается на эти файлы через `StyleInclude` и merged dictionaries

Это позволяет не превращать стили в неудобный JSON authoring experience, но при этом делать их видимыми для инструментов.

## Модель Работы Editor

`ArxisStudio.Editor` это design-time tool, а не runner пользовательского приложения.

Базовое правило такое:

- проект используется как источник типов, ресурсов и style-файлов
- пользовательский event code и runtime logic выполняться не должны

Сейчас editor умеет:

- открывать `.csproj` или `.sln`
- индексировать `.arxui` и `.axaml` файлы
- открывать `.arxui` прямо из контекста проекта
- записывать изменения обратно в открытый `.arxui`
- применять локальные ресурсы и стили в preview
- загружать внешние `.axaml` style/resource файлы для preview

При этом editor пока ещё не претендует на полное runtime parity:

- preview всё ещё частичный
- загрузка project assemblies пока не полностью изолирована
- пользовательская runtime-логика не должна исполняться

## Дорожная Карта Live Preview

Целевой workflow выглядит так:

1. Открывается `.sln` или `.csproj`
2. Обнаруживаются типы проекта, ресурсы, `.arxui` и `.axaml`
3. Пользователь дважды кликает по `.arxui`
4. В дизайнере появляется preview, максимально близкий к реальному Avalonia-результату

Чтобы дойти до этого состояния, остаются следующие шаги:

1. Завершить работу с проектным контекстом в editor
2. Добавить assembly-based type resolution для пользовательских project types
3. Подтянуть parity preview для ресурсов, стилей, шаблонов и bindings
4. Сохранить safe-preview модель без выполнения пользовательской runtime-логики
5. Добавить диагностику и incremental refresh

## Шаблонный Проект

`ArxisStudio.Template` существует специально для проверки editor workflow.

Он включает:

- `.arxui` layout documents
- внешние `.axaml` styles
- внешние `.axaml` resource dictionaries
- `Application` document в формате `.arxui`

Сейчас именно этот проект является основным reference case для:

- project loading в editor
- style/resource-aware preview
- source generation сразу по нескольким document kinds

## Сборка И Запуск

Требования:

- .NET SDK 9.0

Команды:

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Markup.sln
dotnet run --project ArxisStudio.Sample/ArxisStudio.Markup.Sample.csproj
dotnet run --project ArxisStudio.Editor/ArxisStudio.Editor.csproj
dotnet run --project ArxisStudio.Template/ArxisStudio.Markup.Template.csproj
```

## Текущее Состояние

- solution успешно собирается
- generator tests проходят
- build-time generation поддерживает `Application`, `Control`, `Window`, `Styles` и `ResourceDictionary`
- generator поддерживает host-level `Styles` и `Resources`
- generator поддерживает `StyleInclude` и merged resource dictionaries
- в `ArxisStudio.Sample` есть рабочие `.arxui`-примеры
- `ArxisStudio.Template` использует `.arxui` и для `Application`, и для screen UI
- `ArxisStudio.Editor` умеет загружать проектовый контекст и открывать `.arxui` из проекта
- editor preview уже применяет локальные стили и ресурсы, но пока ещё не имеет полного build/runtime parity

## Замечания

- `Class` обязателен для build-time генерации `Application`, `Control` и `Window` документов.
- `SchemaVersion: 1` пока всё ещё следует рассматривать как внутренний evolving contract.
- `ArxisStudio.Editor` пока следует понимать как активный прототип, а не как finished IDE-grade designer.
