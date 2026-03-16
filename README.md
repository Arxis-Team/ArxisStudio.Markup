# ArxisStudio.Markup

`ArxisStudio.Markup` — набор библиотек для model-driven описания Avalonia UI через документы `.arxui`, их сериализации, генерации кода, runtime/design-time загрузки и визуального редактирования.

Проект не пытается заменить Avalonia XAML как формат ручной разработки. Его задача — дать стабильную объектную модель документа, пригодную для:

- визуального конструктора;
- трансформаций дерева UI;
- round-trip сериализации;
- compile-time генерации `InitializeComponent()`;
- безопасного preview без выполнения пользовательского runtime-кода.

## Архитектура

Ядро разделено на пять библиотек:

- `ArxisStudio.Markup`
  Нейтральная модель документа `.arxui`: `UiDocument`, `UiNode`, `UiValue`, `UiStyles`, `UiResources`, `$design`.

- `ArxisStudio.Markup.Json`
  JSON serializer/deserializer формата `.arxui`.

- `ArxisStudio.Markup.Json.Loader`
  Loader, который строит Avalonia object tree из `UiNode` и применяет свойства, ресурсы, стили и ассеты.

- `ArxisStudio.Markup.Generator`
  Roslyn source generator, генерирующий C#-код для partial-классов по `.arxui`.

- `ArxisStudio.Markup.Workspace`
  Roslyn-based слой анализа проекта: индекс типов, индекс `.arxui`, семантическая валидация и framework type catalog для editor tooling.

Инструментальные проекты:

- `ArxisStudio.Designer`
  Design surface, selection, adorner layer и взаимодействие визуального конструктора.

- `ArxisStudio.Editor`
  Desktop-приложение визуального редактора.

- `ArxisStudio.Template`
  Шаблонный Avalonia-проект для открытия в editor.

- `ArxisStudio.Sample`
  Пример использования библиотеки в приложении.

- `ArxisStudio.Tests`
  Тесты сериализации, генерации и диагностик.

## Для чего нужен `.arxui`

Обычный XAML удобен для ручного написания UI, но плохо подходит как внутренний persistence format визуального редактора. Для конструктора нужен не текстовый DSL, а структурированная модель документа.

`.arxui` в этой системе:

- хранит дерево UI и значения свойств;
- используется как вход для source generator;
- используется как формат сохранения editor-а;
- может быть загружен напрямую без предварительной компиляции;
- позволяет строить preview из данных, а не через запуск пользовательского представления.

## Поддерживаемые типы документов

Поле `Kind` определяет семантический тип документа.

Сейчас поддерживаются:

- `Application`
- `Control`
- `Window`
- `Styles`
- `ResourceDictionary`

Корневой CLR-тип задаётся через `Root.TypeName`.

## Модель `.arxui`

Текущий контракт поддерживает:

- создание объектов по CLR-имени типа;
- вложенные узлы;
- scalar properties;
- свойства-коллекции, например `Children`;
- attached properties, например `Grid.Row`, `Canvas.Left`;
- ссылки на ресурсы через `$resource`;
- привязки через `$binding`;
- ссылки на ассеты через `$asset`;
- host-level `Styles`;
- host-level `Resources`;
- `StyleInclude`;
- merged resource dictionaries;
- keyed resources;
- design-time metadata через `$design`.

### `$design`

`$design` — служебный канал design-time metadata, аналогичный по смыслу `d:*` в XAML. Эти данные предназначены для editor/designer и не должны менять production semantics.

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

## Loader

`ArxisStudio.Markup.Json.Loader` отвечает только за загрузку документа и построение Avalonia object tree.

Публичная точка входа:

```csharp
public sealed class ArxuiLoader
{
    public Control? Load(UiNode node, ArxuiLoadContext context);
}
```

Loader:

- создаёт Avalonia-объекты по `TypeName`;
- применяет CLR- и Avalonia-свойства;
- поддерживает коллекции и вложенные узлы;
- применяет `Resources` и `Styles`;
- загружает внешние `.axaml`-словари и стили;
- разрешает `$asset`;
- умеет fallback на другой `.arxui`-документ по `Class`;
- создаёт preview-shell для top-level типов, например `Window`.

Loader не должен содержать логику визуального конструктора. Selection, рамки, handles, drag/drop и editor interaction относятся к `ArxisStudio.Designer` и `ArxisStudio.Editor`.

## Generator

`ArxisStudio.Markup.Generator` — инкрементальный Roslyn source generator.

Обычный сценарий:

1. В проекте объявляется `partial`-класс Avalonia-представления.
2. Создаётся соответствующий `.arxui`.
3. В `Class` указывается полное CLR-имя целевого типа.
4. `.arxui` подключается в проект как `AdditionalFiles`.
5. Generator генерирует `InitializeComponent()`.

Generator проверяет:

- корректность JSON-документа;
- наличие `Class` там, где он обязателен;
- совместимость `Kind` и корневого типа;
- наличие target type;
- `partial`-модификатор;
- совместимость root type и target type;
- конфликты нескольких `.arxui` для одного CLR-типа.

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

## Roslyn в проекте

Roslyn используется в проекте не как замена runtime loader-а, а как слой анализа кода и проекта.

### Где Roslyn уже используется

- `ArxisStudio.Markup.Generator`
  - source generation;
  - поиск target types;
  - compile-time diagnostics;
  - валидация `.arxui` относительно кода проекта.

- `ArxisStudio.Markup.Workspace`
  - индексация типов проекта;
  - анализ наследования;
  - построение metadata свойств;
  - индекс `.arxui`-документов;
  - семантическая валидация для editor;
  - построение toolbox-каталога.

### Что Roslyn даёт этому проекту

- можно анализировать проект без запуска пользовательского кода;
- можно понимать типы, даже если preview не должен создавать их runtime-экземпляры;
- можно распознавать наследование вроде `UserProfile : MyControl : UserControl`;
- можно строить `Toolbox` и `Inspector` не по хардкоду, а по реальной кодовой модели проекта;
- можно выполнять раннюю валидацию `.arxui` до попытки preview;
- можно поддерживать часть editor tooling даже при отсутствии полной runtime-сборки представления.

### Что Roslyn не заменяет

Roslyn не создаёт реальные Avalonia-объекты. Поэтому он не заменяет `ArxuiLoader`.

Практическое разделение такое:

- Roslyn нужен для анализа проекта;
- Reflection и runtime API нужны для инстанцирования preview;
- Loader отвечает за построение дерева объектов;
- Workspace отвечает за понимание проекта.

### `ArxisStudio.Markup.Workspace`

`ArxisStudio.Markup.Workspace` строит design-time модель проекта.

Сейчас он включает:

- `RoslynWorkspaceService`
  - загружает `.csproj`/`.sln`;
  - индексирует `*.cs`;
  - строит `TypeMetadata` для project types;
  - индексирует `.arxui`-документы;
  - формирует `WorkspaceContext`.

- `FrameworkTypeCatalogService`
  - строит каталог стандартных Avalonia controls;
  - используется editor-ом для toolbox.

- `ArxuiSemanticValidator`
  - выполняет базовую семантическую проверку документа в контексте проекта.

### Почему это важно для editor

Без `Workspace` editor был бы вынужден:

- жёстко хардкодить toolbox;
- читать только уже существующие свойства из JSON;
- плохо понимать пользовательские типы проекта;
- зависеть от runtime reflection там, где нужен статический анализ.

С `Workspace` editor может:

- показывать и framework controls, и project custom controls в одном toolbox;
- строить inspector по metadata типа;
- выбирать previewable startup document умнее;
- подсказывать семантические проблемы до preview.

## Стили и ресурсы

Рекомендуемая схема — гибридная.

`.arxui` должен описывать:

- дерево UI;
- composition;
- узловые ресурсы;
- ссылки на внешние style/resource файлы;
- bindings и assets.

`.axaml` остаётся подходящим местом для:

- глобальных стилей;
- сложных селекторов;
- крупных resource dictionaries;
- шаблонов и другой человекочитаемой XAML-разметки.

Это позволяет не дублировать язык Avalonia styling в JSON, но делает его доступным для preview и generator через `StyleInclude` и merged dictionaries.

## Editor

`ArxisStudio.Editor` — design-time инструмент, а не runner пользовательского приложения.

Базовые правила:

- проект используется как источник типов, `.arxui`, `.axaml` и assets;
- preview строится через `ArxuiLoader`;
- пользовательский code-behind и event handlers не исполняются;
- design-time chrome живёт отдельно от production visual tree.

На текущем этапе editor умеет:

- открывать `.csproj` и `.sln`;
- индексировать `.arxui`, `.axaml` и типы проекта;
- строить preview из `.arxui`;
- применять локальные стили и ресурсы;
- загружать внешние `.axaml` для preview;
- учитывать `$design.SurfaceWidth` / `$design.SurfaceHeight`;
- учитывать `$design.Hidden` / `$design.IgnorePreviewInput`;
- синхронизировать preview и outline;
- строить toolbox из framework types и project types;
- строить inspector на основе metadata типа.

Текущие ограничения:

- preview ещё не эквивалентен полноценному runtime приложения;
- кастомные `axaml + cs` контролы пока не поддержаны как полноценный design-time runtime;
- вложенные include-цепочки в `.axaml` ещё не покрыты полностью;
- bindings и сложные сценарии styling поддерживаются частично.

## Сборка

Основные команды:

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Tests/ArxisStudio.Markup.Generator.Tests.csproj
```

Запуск editor:

```bash
dotnet run --project ArxisStudio.Editor/ArxisStudio.Editor.csproj
```

Запуск template-приложения:

```bash
dotnet run --project ArxisStudio.Template/ArxisStudio.Markup.Template.csproj
```

## Текущее направление развития

Ближайшие архитектурные задачи:

- углубить `Workspace`-слой и расширить semantic validation;
- улучшить поддержку пользовательского наследования и custom controls;
- вынести из loader всё, что относится к дизайнерскому взаимодействию;
- развить design surface и container-specific behavior;
- улучшить цепочку загрузки ресурсов и стилей для сложных `.axaml`-сценариев.
