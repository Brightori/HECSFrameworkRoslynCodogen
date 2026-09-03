# Структура проекта

> Репозиторий: `HECSFrameworkRoslynCodogen` (`git@github.com:Brightori/HECSFrameworkRoslynCodogen.git`, ветка `main`)
> Тип: консольное .NET 7 приложение (кодогенератор на базе Roslyn) для HECS Framework.

---

## Верхний уровень

```
HECSFrameworkRoslynCodogen/
├── RoslynHECS.sln              — solution (один проект)
├── global.json                 — pin SDK (значение "3.1", фактически проект собирается под net7.0)
├── NuGet.config                — источники пакетов
├── .gitmodules                 — сабмодуль HECSCore
├── .gitignore                  — bin/obj/.vs
├── .github/                    — папка есть, workflow'ов внутри нет
├── docs/                       — 📄 эта документация
└── RoslynHECS/                 — единственный проект
```

## Проект `RoslynHECS/`

```
RoslynHECS/
├── RoslynHECS.csproj           — net7.0, Exe, SelfContained, win-x64, PublishSingleFile, AllowUnsafeBlocks
├── Program.cs                  — 🚪 ТОЧКА ВХОДА: парсинг, сбор типов, запись файлов
├── CodogeneratorRoslynPart.cs  — 🏭 ЯДРО ГЕНЕРАЦИИ (~3100 строк, partial class CodeGenerator)
├── EntitiesWorld.cs            — генерация ComponentsWorldPart.cs (partial class World)
├── FastWorldPart.cs            — генерация FastWorldPart.cs + провайдеров IFastComponent
├── DataTypes/                  — модели данных генератора
├── Helpers/                    — утилиты работы с синтаксисом
├── Properties/                 — launchSettings.json + профили публикации (Windows / MacOs)
└── HECSCore/                   — 🔗 git-сабмодуль (HECSFrameworkCore)
```

### `RoslynHECS/` — корневые файлы

| Файл | Строк | Назначение |
|---|---:|---|
| `Program.cs` | ~1170 | `Main`, разбор аргументов, парсинг `.cs`-файлов целевого проекта, три Roslyn-визитора, построение графов наследования (`LinkedNode`, `LinkedInterfaceNode`, `LinkedGenericInterfaceNode`), `SaveFiles()` |
| `CodogeneratorRoslynPart.cs` | ~3100 | Все генераторы: SystemBindings, TypesMap, HECSMasks, резолверы сериализации, BluePrints, CommandsMap/ShortID, Documentation |
| `EntitiesWorld.cs` | 80 | `GetEntitiesWorldPart()` → `partial void FillRegistrators()` со списком `ComponentProviderRegistrator<T>` |
| `FastWorldPart.cs` | 88 | `GetFastWorldPart()` → `partial void FillTypeRegistrators()`, плюс `<X>FastProvider.cs` для каждого `IFastComponent` |

### `RoslynHECS/DataTypes/`

| Файл | Что описывает |
|---|---|
| `GatheredField.cs` | `struct GatheredField` — собранное поле/свойство: тип, имя, `Order`, `ResolverName`, флаги `IsPrivate` / `IsSerializable` / `IsPartial`. Читает атрибут `[Field(order[, typeof(Resolver)])]` |
| `MemberNode.cs` | `class MemberNode` — обёртка над `MemberDeclarationSyntax` (поле или свойство) + его атрибуты + `GatheredField`. `IsPublic()` учитывает модификатор сеттера у свойств |
| `LinkedNodeExtended.cs` | `sealed class LinkedNodeExtended : LinkedNode` — узел с раскрытыми членами: собирает поля со всех parts и родителей, обрабатывает `[PartialSerializeField(order, "name"[, "resolver"])]`, флаг `IsPrivateFieldIncluded` |
| `ResolverData.cs` | `struct ResolverData` — пара «тип → имя резолвера» |
| `ShortIDObject.cs` | Объект для карты коротких сетевых идентификаторов (ShortID) |

### `RoslynHECS/Helpers/`

| Файл | Что даёт |
|---|---|
| `SyntaxHelper.cs` | `GetType()` / `GetFieldName()` для field/property, extension `AddUnique(this ISyntax, ISyntax)` — добавление узла без дублей (используется для usings) |
| `LinkedNodeHelper.cs` | `GetLinkedNode(ClassDeclarationSyntax)` — построение `LinkedNode` с parts и рекурсивным родителем (для `[HECSResolver]`-классов) |
| `GetDictionaryHelper.cs` | `GetDictionaryMethod(...)` и `DictionaryBodyRecord(...)` — шаблоны генерации методов-фабрик `Dictionary<K,V>` |

### `RoslynHECS/Properties/`

```
launchSettings.json                       — профиль запуска "RoslynHECS", nativeDebugging
PublishProfiles/WindowsProfile.pubxml     — публикация под Windows
PublishProfiles/MacOs Profile.pubxml      — публикация под macOS
```

---

## Сабмодуль `RoslynHECS/HECSCore/`

Исходники рантайма HECS Framework (`https://github.com/Brightori/HECSFrameworkCore.git`).
Генератор использует его двояко: как **библиотеку** (движок построения синтаксиса) и как
**эталон типов** (`typeof(TypesProvider).Name`, `typeof(HECSMask).Name`, `IndexGenerator` и т.п.).

### Критично для генератора

| Путь | Роль |
|---|---|
| `HECSGenerator/SyntaxTree.cs` | 🧱 DSL построения кода: `ISyntax`, `TreeSyntaxNode`, `TabSimpleSyntax`, `LeftScopeSyntax`, `RightScopeSyntax`, `UsingSyntax`, `NameSpaceSyntax`, `ParagraphSyntax`, `CompositeSyntax`, `SimpleSyntax`, `TabSpaceSyntax`, `ModificatorSyntax`, `FieldMember` |
| `HECSGenerator/CParse.cs` | Константы языка (`LeftScope`, `Comma`, `Tab`, `Quote`, …), `ExtractModifier`, `GetObjectRecursive`, `FieldMembers.TryGetKnownType` |
| `HECSGenerator/CodeGenerator.cs` | Вторая часть `partial class CodeGenerator` — reflection-ветка генерации (в Roslyn-режиме почти не используется, `StartGeneration()` закомментирован) |
| `IndexGenerator.cs` | `GenerateIndex(string)` — детерминированный хеш имени типа → `TypeHashCode`. **Тот же алгоритм должен работать в рантайме** |
| `HECSMask.cs`, `ComponentMaskAndIndex.cs` | Формат маски компонентов (ulong-битовые поля) |
| `Providers/TypesProvider.cs`, `Providers/MaskProvider.cs` | Partial-классы, которые генератор дополняет |
| `ComponentContext.cs` | Partial-класс контекста компонентов |

### Ядро рантайма (для понимания того, что генерируем)

| Раздел | Содержимое |
|---|---|
| `Entity.cs`, `IEntity.cs`, `EntityModel.cs`, `EntityManager.cs`, `EntityService.cs`, `World.cs`, `EntityFilter.cs` | Сущности, миры, фильтры |
| `IComponent.cs`, `BaseComponent.cs`, `Components/` | Компоненты и базовые компоненты (`AbilityOwner`, `Predicates`, `PoolableTag`, `ActorContainerID`, …) |
| `ISystem.cs` (+ `BaseSystem`), `Systems/` | Системы: пулинг, удаление сущностей/компонентов, ожидающие команды |
| `ICommand.cs`, `Commands/`, `CommandsServices/` | Команды и сервисы их доставки (`EntityGlobalCommandService`, `EntityLocalCommandService`) |
| `ComponentsServices/` | `ComponentsService`, локальные и глобальные слушатели компонентов |
| `GlobalUpdateSystem/` | `GlobalUpdateSystem` + модули: Default, Fixed, Late, Async, DeltaTime, GlobalStart, Reacts, Priority, Dispatch, JobSystem |
| `Abilities/` | Абилки: `BaseAbilitySystem`, `CompositeAbilitySystem`, `BasePassiveAbilitySystem`, теги, команды, `BaseAbilitiesHolderComponent` |
| `Counters/` | Счётчики: `ModifiableFloat/IntCounter`, `CountersHolderComponent`, `CountersHolderSystem`, команды модификаторов |
| `Modifiers/` | `IModifier`, `ModifiersContainer`, `ModifiersCalculation`, `BaseFloatModifier` |
| `Awaiters/` | `Awaiter`, `AsyncBuilder`, `JobsSystem`, `WaitForSeconds`, `WaitUntil`, `WaitWhile` |
| `Collections/` | `ConcurrencyList`, `ArrayHelpers`, `HashHelpers`, `Remover` |
| `Helpers/` | Расширения сущностей, коллекций, векторов/кватернионов, `ReactiveValue`, `ReadonlyList`, `RequiredAttribute` |
| `DocumentationFeature/` | `[Documentation]`, `DocHelper`, `HECSDocumentation` |
| `Debugger/` | `HECSDebug`, `AssertionException` |
| `Rewards/`, `Predicate.cs`, `Animation/`, `ScenarioFeature/` | Прикладные подсистемы |

> **Про `.meta`-файлы.** Многие файлы в `HECSCore` продублированы `*.meta` — это артефакты Unity. Генератор их игнорирует; в git они хранятся ради интеграции ядра в Unity-проекты.

---

## Что НЕ входит в репозиторий

`bin/`, `obj/`, `.vs/` — в `.gitignore`. Их содержимое (включая `RoslynHECS.exe`) — результат сборки.

---

## Карта «файл → что генерирует»

| Исходник генератора | Выходной файл(ы) |
|---|---|
| `CodogeneratorRoslynPart.GenerateTypesMapRoslyn` | `TypeProvider.cs` |
| `CodogeneratorRoslynPart.GetSystemBindsByRoslyn` | `SystemBindings.cs` |
| `CodogeneratorRoslynPart.GenerateHecsMasksRoslyn` | `HECSMasks.cs` |
| `EntitiesWorld.GetEntitiesWorldPart` | `ComponentsWorldPart.cs` |
| `FastWorldPart.GetFastWorldPart` | `FastWorldPart.cs` |
| `FastWorldPart.GetProvidersForFastComponent` | `FastComponentsProviders/<X>FastProvider.cs` |
| `CodogeneratorRoslynPart.GetSerializationResolvers` | `Resolvers/<X>Resolver.cs` |
| `CodogeneratorRoslynPart.GetResolverMap` | `MapResolver.cs` |
| `CodogeneratorRoslynPart.GetCustomResolversMap` | `CustomAndUniversalResolvers.cs` |
| `CodogeneratorRoslynPart.GenerateNetworkCommandsAndShortIdsMap` | `CommandsMap.cs` |
| `CodogeneratorRoslynPart.Generate*BluePrints` / `Get*BluePrints` | BluePrints в `Assets/Scripts/BluePrints/...` |
| `CodogeneratorRoslynPart.GetBluePrintsProvider` | `BluePrintsProvider.cs` |

Подробности — в [CODEGEN_PIPELINE.md](CODEGEN_PIPELINE.md).
