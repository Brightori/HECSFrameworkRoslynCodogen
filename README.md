# RoslynHECS

Внешний кодогенератор для **HECS Framework** на базе Roslyn.

Консольное .NET 7 приложение: получает путь к папке с исходниками проекта (Unity-клиент или сервер), парсит все `.cs` файлы, строит модель типов — компонентов, систем, команд, интерфейсов — и генерирует boilerplate, который вручную писать невозможно: контейнеры типов (фабрика, регистрация в мире, биндинги систем), резолверы сериализации, blueprint'ы и карту сетевых команд.

```
исходники проекта ──► Roslyn parse ──► граф типов ──► *.cs в HECSGenerated/
```

---

## Быстрый старт

```bash
git clone --recurse-submodules git@github.com:Brightori/HECSFrameworkRoslynCodogen.git
cd HECSFrameworkRoslynCodogen
dotnet build RoslynHECS.sln

# Unity-проект
dotnet run --project RoslynHECS -- path:D:\MyGame\Assets\

# Серверный проект
dotnet run --project RoslynHECS -- path:/repo/Server/ server
```

Без `--recurse-submodules` проект не соберётся: сабмодуль `RoslynHECS/HECSCore` содержит движок построения синтаксиса.

### Аргументы

| Аргумент | Действие |
|---|---|
| `path:<путь>` | папка с исходниками. **Без него берётся хардкод из `Program.cs`** |
| `server` | генерат в `<path>/HECSGenerated/` вместо `<path>/Scripts/HECSGenerated/` |
| `no_blueprints` | не генерировать blueprint'ы |
| `no_resolvers` | не генерировать резолверы сериализации |
| `no_commands` | не генерировать `CommandsMap.cs` |
| `defines:A;B` | символы препроцессора для парсера: без них код под `#if` не виден генератору |
| `force_rebuild` | очистить `Containers/`, `Resolvers/`, `FastComponentsProviders/` и записать всё заново без сверки с диском |

> Нюанс: при запуске **вообще без аргументов** `CommandsMap.cs` не генерируется — см. [CODEGEN_PIPELINE.md §0](docs/CODEGEN_PIPELINE.md).

---

## Документация

| Документ | О чём |
|---|---|
| 📁 [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) | Дерево репозитория, назначение каждой папки и файла, карта «исходник → генерат» |
| 🏛 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Слои, модель типов (`LinkedNode` и Ко), DSL построения кода, слабые места |
| ⚙️ [docs/CODEGEN_PIPELINE.md](docs/CODEGEN_PIPELINE.md) | Пошаговый пайплайн: аргументы → парсинг → графы → каждый выходной файл. Атрибуты, маски, хеши |
| 🧩 [docs/HECS_CORE_CONCEPTS.md](docs/HECS_CORE_CONCEPTS.md) | Рантайм HECS: Entity/Component/System/World, команды, реакции, инварианты генерат↔рантайм |
| 🛠 [docs/HOWTO.md](docs/HOWTO.md) | Рецепты: добавить компонент, подписать систему, настроить сериализацию, добавить новый генерируемый файл, отладка |
| 🤖 [CLAUDE.md](CLAUDE.md) | Контекст для ИИ-ассистентов, работающих с этим репозиторием |

---

## Что генерируется

| Файл | Содержимое |
|---|---|
| `Containers/<X>Container.cs` | контейнер компонента (хеш, фабрика, регистрация провайдера в `World`, при резолверах — их регистрация) или системы (фабрика, подписки на команды, автобиндинг `[Required]`/`[Single]` полей) |
| `Containers/<X>FastContainer.cs` + `FastComponentsProviders/` | регистрация `IFastComponent` в мире и Unity-провайдеры |
| `Containers/<X>ResolverContainer.cs` | регистрация кастомного резолвера (`[HECSManualResolver]`, `[HECSResolver]`) |
| `Resolvers/<X>Resolver.cs` | резолверы бинарной сериализации компонентов |
| `ResolversMapRuntime.cs` | инфраструктура `ResolversMap`: словари по хешам, наполняются контейнерами |
| `CommandsMap.cs` | карта сетевых команд + ShortID |
| `BluePrints/…` + `BluePrintsProvider.cs` | Unity-blueprint'ы компонентов, систем, предикатов, экшенов |

Каждый контейнер регистрируется строкой в рукописном `TypeContainersRegistry` (ядро), без рефлексии; индексы и маски считаются в рантайме. Старые монолиты (`TypeProvider.cs`, `SystemBindings.cs`, `HECSMasks.cs`, `ComponentsWorldPart.cs`, `FastWorldPart.cs`, `MapResolver.cs`, `CustomAndUniversalResolvers.cs`, `WorldRegistration.cs`) не генерируются и удаляются при прогоне.

---

## Структура в двух словах

```
RoslynHECS/
├── Program.cs                    — точка входа: парсинг, графы типов, запись файлов
├── CodogeneratorRoslynPart.cs    — основные генераторы (partial class CodeGenerator)
├── ContainersGeneration.cs       — контейнеры типов и ResolversMapRuntime.cs
├── FastWorldPart.cs              — Unity-провайдеры IFastComponent
├── DataTypes/                    — LinkedNodeExtended, MemberNode, GatheredField, ResolverData, ShortIDObject
├── Helpers/                      — SyntaxHelper, LinkedNodeHelper, GetDictionaryHelper
└── HECSCore/  (git submodule)    — рантайм HECS + DSL построения синтаксиса
```

---

## На что обратить внимание

- Генератор работает **по синтаксису**, не по семантике: типы матчатся по строке имени в base-list. Алиасы и полностью квалифицированные имена ломают матчинг.
- **Индексы компонентов процессно-локальны** — их назначает рантайм, наружу уходят только `TypeHashCode` и ShortID. ShortID нумеруются по имени типа: новый сетевой тип сдвигает номера, клиент и сервер должны генерироваться с идентичным набором.
- Директории генерата (`Containers/`, `Resolvers/`, `FastComponentsProviders/`) принадлежат генератору: каждый прогон перезаписывает лишь изменённые файлы и удаляет осиротевшие (файлы удалённых/переименованных типов, с `.meta`). **Целиком очищаются** только при `force_rebuild`.
- Ошибки записи файлов **не прерывают** работу — ищите в консоли `we cant save file to`.

Подробности и полный список подводных камней — в [ARCHITECTURE.md](docs/ARCHITECTURE.md#известные-архитектурные-слабости).

---

Сабмодуль ядра: [HECSFrameworkCore](https://github.com/Brightori/HECSFrameworkCore)
