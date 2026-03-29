# ArxisStudio.Markup

`ArxisStudio.Markup` — набор библиотек для model-driven описания Avalonia UI через документы `.arxui`, их сериализации, runtime-загрузки и compile-time генерации кода.

Проект ориентирован на создание визуального конструктора, поэтому ключевой приоритет — **стабильный и независимый API** библиотек.

## Состав

### `ArxisStudio.Markup`
Независимый контракт модели документа:
- `UiDocument`, `UiNode`, `UiValue`
- `UiStyles`, `UiResources`
- binding/resource/asset/design metadata

Пакет не зависит от Avalonia и инфраструктуры редактора.

### `ArxisStudio.Markup.Json`
JSON codec для `.arxui`:
- `ArxuiSerializer.Deserialize(string json)`
- `ArxuiSerializer.Serialize(UiDocument document)`

Публичный API не протекает типами JSON-движка.

### `ArxisStudio.Markup.Json.Loader`
Runtime builder из `UiNode` в дерево Avalonia-контролов:
- `ArxuiLoader.Load(UiNode node, ArxuiLoadContext context)`
- расширения через `ITypeResolver`, `IAssetResolver`, `IMarkupDocumentResolver`, `IPathResolver`, `ITopLevelControlFactory`

Loader не зависит от инфраструктуры IDE; project-specific поведение подключается через адаптеры.

### `ArxisStudio.Markup.Generator`
Roslyn source generator для `InitializeComponent()` по `.arxui`:
- проверка контракта документа
- валидация `Kind`/`Class`/`Root`
- генерация кода и диагностик `ADG*`

## Принципы API

1. Ядро модели (`ArxisStudio.Markup`) изолировано от UI/framework.
2. JSON-пакет отвечает только за codec.
3. Loader расширяется через интерфейсы, а не через жёсткие зависимости.
4. Generator публикует диагностируемое поведение и предсказуемый контракт.

## Быстрый старт

### 1) Десериализация `.arxui`

```csharp
using ArxisStudio.Markup.Json;

var json = File.ReadAllText("Views/MainWindow.arxui");
var document = ArxuiSerializer.Deserialize(json);
```

### 2) Preview через loader

```csharp
using ArxisStudio.Markup.Json.Loader;
using ArxisStudio.Markup.Json.Loader.Services;

var loader = new ArxuiLoader();
var control = loader.Load(
    document!.Root,
    new ArxuiLoadContext
    {
        TypeResolver = new ReflectionTypeResolver(),
        AssetResolver = new DefaultAssetResolver(),
        PathResolver = new ProjectContextPathResolver(projectDirectory, assemblyName),
        TopLevelControlFactory = new DefaultTopLevelControlFactory(),
        Options = new ArxuiLoadOptions
        {
            AllowAssets = true,
            AllowExternalIncludes = true,
            AllowDocumentFallback = true,
            AllowBindings = true
        }
    });
```

### 3) Генерация `InitializeComponent()`

Подключите `.arxui` как `AdditionalFiles` и generator как analyzer.

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.arxui" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\ArxisStudio.Markup.Generator\ArxisStudio.Markup.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Диагностики generator

Основные коды:
- `ADG0001` — ошибка JSON
- `ADG0002` — тип не найден
- `ADG0005` — отсутствует `Class` (где обязателен)
- `ADG0006` — target class не найдена
- `ADG0007` — target class не `partial`
- `ADG0008`/`ADG0010`/`ADG0011` — несовместимость `Kind`/`Root`/`Class`
- `ADG0009` — дубликат `.arxui` для одного CLR-типа

## Сборка и тесты

```bash
dotnet build ArxisStudio.Markup.sln
dotnet test ArxisStudio.Tests/ArxisStudio.Markup.Generator.Tests.csproj
```

## Текущее направление

- дальнейшая стабилизация публичного API библиотек
- расширение тестов loader на binding/asset/null-assignment сценарии
- подготовка package-first поставки для интеграции в внешний конструктор
