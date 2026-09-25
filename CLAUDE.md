# CLAUDE.md — контекст для ИИ-ассистентов

Читай этот файл первым при работе с репозиторием `HECSFrameworkRoslynCodogen`.

## Что это

Standalone-кодогенератор на Roslyn для HECS Framework. Консольное .NET 7 приложение: парсит `.cs` файлы целевого проекта (Unity-клиент или сервер) и генерирует boilerplate — контейнеры типов (файл на компонент/систему: фабрика, регистрация в мире, биндинги систем), резолверы сериализации, Unity-blueprint'ы, карту сетевых команд.

**Это НЕ** Roslyn Source Generator, **НЕ** MSBuild-таск, **НЕ** Unity Editor-скрипт. Запускается руками/скриптом как `.exe` с аргументом `path:`.

## Где что лежит

| Путь | Что |
|---|---|
| `RoslynHECS/Program.cs` | точка входа, парсинг, построение графов типов, `SaveFiles()` |
| `RoslynHECS/CodogeneratorRoslynPart.cs` | ~1570 строк, основные генераторы, `partial class CodeGenerator` |
| `RoslynHECS/ContainersGeneration.cs` | контейнеры типов (`Containers/*.cs`) и `ResolversMapRuntime.cs` — ещё одна часть `CodeGenerator` |
| `RoslynHECS/FastWorldPart.cs` | Unity-провайдеры `IFastComponent` — ещё одна часть `CodeGenerator` |
| `RoslynHECS/DataTypes/`, `Helpers/` | модель данных и утилиты |
| `RoslynHECS/HECSCore/` | **git-сабмодуль**, отдельный репозиторий HECSFrameworkCore |
| `RoslynHECS/HECSCore/HECSGenerator/SyntaxTree.cs` | DSL `ISyntax` для построения кода |
| `docs/` | подробная документация |

## Обязательный контекст перед правками

1. **Генератор syntax-only.** `CSharpCompilation` создаётся без ссылок на сборки. Типы сравниваются **по строке**: `x.BaseList.Types.Any(z => z.ToString() == "BaseComponent")`. Не предлагай решения через `ISymbol`/`SemanticModel` без явной оговорки, что это потребует переписать компиляцию.
2. **Глобальное статическое состояние.** Генераторы читают `Program.componentOverData`, `Program.classes`, `Program.componentsDeclarations` напрямую. Это осознанный стиль, не «плохая практика для рефакторинга по пути».
3. **Индекс компонента — процессно-локальный.** Его (и `HECSMask.Index`) назначает `TypesProvider.Build()` в рантайме по порядку регистрации контейнеров; порядок `Program.componentsDeclarations` на него не влияет. Наружу (сейвы, сеть) уходят только `TypeHashCode` и ShortID. ShortID нумеруются в `GetShortIdPart` по имени типа (`StringComparer.Ordinal`) — новый сетевой тип сдвигает номера, клиент и сервер генерируются с одинаковым набором.
4. **DSL с отложенным заполнением.** Пустой `TreeSyntaxNode body` вставляется в дерево, а наполняется ниже по коду — порядок вставки ≠ порядок наполнения. Не «чини» это как баг.
5. **Инварианты генерат↔рантайм.** Нельзя менять: алгоритм `IndexGenerator.GetIndexForType`; реестр `TypeContainersRegistry` и его `Add` (генерат дописывает partial-часть со строкой на контейнер); контракты `ITypeContainer`, `IComponentContainer`, `ISystemContainer`, `IFastComponentContainer`, `IResolverContainer`; статические `ComponentProvider<T>.RegisterWorld`, `FastComponentProvider<T>.RegisterWorld/UnRegisterWorld`; методы `ResolversMap.RegisterResolver_<X>` / `RegisterCustom_<X>` (internal, их вызывают контейнеры); имена `ComponentBluePrintContainer<T>`, `SystemBluePrint<T>`, `FastComponentMonoProvider<T>`.
6. **`HECSCore` — сабмодуль.** Правки там влияют на все проекты на HECS. Не редактируй его «мимоходом»; если правишь — скажи об этом явно и напомни про push + обновление указателя.

## Язык и стиль кода

- Комментарии и консольный вывод в проекте **на русском** — сохраняй язык при правках рядом.
- Смешанный нейминг: `componentOverData`, `alrdyAtContext`, `needeType` — опечатки в именах существующие, не переименовывай без запроса.
- Строковые константы имён интерфейсов объявлены как `public const string` в начале `CodeGenerator` — новые добавляй туда же.

## Типовые задачи

| Задача | Куда идти |
|---|---|
| Добавить новый генерируемый файл | метод в `CodeGenerator` → константа имени в `Program.cs` → вызов в `SaveFiles()` |
| Новый react-интерфейс для биндинга | `const string` в `CodeGenerator` + `case` в `ProcessReacts` |
| Новый вид команды | `Program.ProcessStructs` |
| Изменить критерий «что считается компонентом» | `Program.GatherComponents` |
| Новый аргумент CLI | `Program.CheckArgs` |
| Фильтр входных файлов | `Program.Main`, `.Where(...)` |

## Известные ловушки (не «чини», а учитывай)

- `CheckArgs` при пустом `args` делает ранний `return`. `commandMapneeded` инициализирован `false`, `resolversNeeded`/`bluePrintsNeeded` — `true`. Поэтому «без аргументов» и «с `path:`» дают разный набор файлов.
- Хардкод путей в `Program.ScriptsPath` / `HECSGenerated` — на машину автора.
- Фильтр исключений использует `"\\Plugins"` — Windows-only.
- Парсинг идёт без символов препроцессора: код под `#if` не виден генератору, пока символы не переданы аргументом `defines:`.
- `SaveToFile` глотает исключения, пишет `we cant save file to …` и продолжает.
- Осиротевшие файлы убираются каждый прогон: после записи из `Containers/`, `Resolvers/`, `FastComponentsProviders/` удаляются все `.cs` (с `.meta`), которых этот прогон не породил; в прогоне с `no_resolvers` чистится только `Containers/`, а `*FastContainer.cs` / `*ResolverContainer.cs` не трогаются. `CleanDirectory` (полная очистка и запись без сверки) — только при `force_rebuild`. Легаси-файлы (монолиты `TypeProvider.cs`, `SystemBindings.cs`, `HECSMasks.cs`, `WorldRegistration.cs` и др., контейнеры со старыми именами `*.Container.cs` / `*.FastContainer.cs` / `*.CustomResolver.cs`) `SaveFiles` удаляет при каждом прогоне.
- Генератор `Documentation.cs` (`GetDocumentationRoslyn`) жив, но из `SaveFiles` не вызывается. Генераторов `MaskProvider.cs` / `ComponentContext.cs` больше нет: `HECSCore/HECSGenerator/CodeGenerator.cs` урезан до `GatherAssembly()` для меню документации.
- Проект таргетит `net9.0`, `global.json` в репозитории нет - берётся SDK по умолчанию.

## Проверка изменений

Автотестов в репозитории нет. Минимальная проверка:

```bash
dotnet build RoslynHECS.sln
dotnet run --project RoslynHECS -- path:<путь к тестовому проекту>
```

Смотреть в выводе: число найденных файлов, `components N`, `systems N`, отсутствие `we cant save file to`. Затем — компилируется ли целевой проект с новым генератом.

## Ссылки

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — слои и модель типов
- [docs/CODEGEN_PIPELINE.md](docs/CODEGEN_PIPELINE.md) — пошаговый пайплайн и все выходные файлы
- [docs/HECS_CORE_CONCEPTS.md](docs/HECS_CORE_CONCEPTS.md) — рантайм HECS
- [docs/HOWTO.md](docs/HOWTO.md) — рецепты и отладка
- [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) — карта файлов
