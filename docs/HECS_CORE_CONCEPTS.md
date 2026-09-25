# Концепции HECS Core

Краткая карта рантайма, ради которого существует генератор. Исходники — в сабмодуле `RoslynHECS/HECSCore/` (репозиторий [HECSFrameworkCore](https://github.com/Brightori/HECSFrameworkCore)).

> Зачем это в документации генератора: чтобы генерировать корректный код, надо понимать контракты, которые он обязан соблюсти — `partial`-точки расширения, контракты контейнеров типов и хеши типов.

---

## Модель: Hybrid ECS

HECS — «гибридный» ECS: компоненты не только данные (есть методы и наследование), системы привязаны к сущности-владельцу, а не только к глобальному миру.

```
World  ──*  IEntity  ──*  IComponent
                     └─*  ISystem
```

---

## Entity

`IEntity` (`HECSCore/IEntity.cs`) — сущность-контейнер.

Ключевое из контракта:

```csharp
int WorldId { get; }        World World { get; }        Guid GUID { get; }
HECSMultiMask ComponentsMask { get; }
ComponentContext ComponentContext { get; }

bool TryGetHecsComponent<T>(HECSMask mask, out T component);  // быстрый путь — рантайм
bool TryGetHecsComponent<T>(out T component);                 // медленный путь — только на ините
T GetOrAddComponent<T>(IEntity owner = null);
T AddHecsComponent<T>(T component, IEntity owner = null, bool silently = false);
void AddHecsSystem<T>(T system, IEntity owner = null);
void RemoveHecsComponent(HECSMask component);

void Command<T>(T command) where T : struct, ICommand;
bool TryGetSystem<T>(out T system);

void Init(bool needRegister = true);
void MigrateEntityToWorld(World world, bool needInit = true);
void HecsDestroy();

bool ContainsMask(ref HECSMask mask);
bool ContainsMask(FilterMask mask);
bool ContainsAnyFromMask(HECSMultiMask mask);
```

Два способа получить компонент — **осознанный выбор**: по маске — O(1)-доступ, дженерик-версия ищет медленнее и предназначена для инициализации. Статических масок `HMasks` больше нет (не генерируются); маску типа выдаёт рантайм (`TypesMap`) после `TypesProvider.Build()`.

## Component

```csharp
public interface IComponent : IHaveOwner
{
    int GetTypeHashCode { get; }
    HECSMask ComponentsMask { get; set; }
    bool IsAlive { get; set; }
    bool IsRegistered { get; }
    void SetIsRegistered();
    void UnRegister();
}

public abstract class BaseComponent : IComponent
{
    protected virtual void ConstructorCall() { }   // хук инициализации
    public int GetTypeHashCode => ComponentsMask.TypeHashCode;
}
```

`IWorldSingleComponent : IComponent` — тег синглтон-компонента мира.

**Для генератора:** класс становится компонентом, если в его base-list встречается строка `BaseComponent` или `IComponent`; дальше наследники подхватываются рекурсивно. Абстрактные компоненты в маски и фабрику не попадают.

## System

```csharp
public interface ISystem : IDisposable, IHaveOwner
{
    Guid SystemGuid { get; }
    void InitSystem();
    int GetTypeHashCode { get; }
    bool IsDisposed { get; }
}

public abstract class BaseSystem : ISystem
{
    public IEntity Owner { get; set; }
    public Guid SystemGuid { get; } = Guid.NewGuid();
    public int GetTypeHashCode => IndexGenerator.GetIndexForType(GetType());  // ← тот же хеш, что в генерате
    public abstract void InitSystem();
}
```

`IHavePause` — `Pause()` / `UnPause()`.

## Mask

`HECSMask` — пара `Index` + `TypeHashCode`. `Index` назначает `TypesProvider.Build()` в рантайме по порядку регистрации контейнеров; он процессно-локален и наружу не уходит.
`HECSMultiMask` — маска сущности (набор всех её компонентов).
`ComponentMaskAndIndex` — пара `{ HECSMask ComponentsMask; string ComponentName; }`, элемент `TypesProvider.MapIndexes` (снаружи — `TypesMap.ComponentsInfo`).

`HMasks` / `HECSMasks.cs` не генерируются; `HMaskDummy`, `MaskProvider`, `IMaskProvider` и `TypesMap.MaskProvider` из ядра удалены.

## Команды

```csharp
public interface ICommand { }
```

Команды — **структуры**. Маркерные интерфейсы определяют способ доставки:

| Интерфейс | Смысл | Куда попадает в генераторе |
|---|---|---|
| `ICommand` | локальная команда сущности | `localCommands` |
| `IGlobalCommand` | глобальная команда мира | `globalCommands` + `localCommands` |
| `INetworkCommand` / `INetworkLocalCommand` | сетевая команда | `globalCommands` + `localCommands` + `networkCommands` |

Реакция на команды со стороны систем — через дженерик-интерфейсы, которые генератор превращает в подписки в контейнере системы (`Containers/<X>Container.cs`):

| Интерфейс системы | Подписка |
|---|---|
| `IReactCommand<T>` | локальный слушатель команды `T` |
| `IReactGlobalCommand<T>` | глобальный слушатель команды `T` |
| `IReactNetworkCommandLocal<T>` / `IReactNetworkCommandGlobal<T>` | сетевые слушатели (только при генерации CommandsMap) |
| `IRequestProvider<T>` / `<T1,T2>` | провайдер запроса |
| `IRequestProcessor<T>` / `<T1,T2>` | обработчик запроса (с ветвлением `#if SERVER`) |

Сервисы доставки: `CommandsServices/EntityLocalCommandService.cs`, `EntityGlobalCommandService.cs`.

## Реакции на компоненты

```csharp
IReactComponent                 // любые компоненты в мире
IReactComponentLocal            // любые компоненты на этой сущности
IReactComponentLocal<T>         // компонент типа T на этой сущности
IReactComponentGlobal<T>        // компонент типа T в этом мире
```

Слушатели: `ComponentsServices/LocalComponentListenersService.cs`, `GlobalComponentListenersService.cs`.

> В текущей версии генератора биндинг этих четырёх интерфейсов **закомментирован** в `ProcessReacts` — подписка выполняется рантаймом, не генератом.

## World и регистрация типов

Регистрация идёт через **контейнеры типов**, без рефлексии. Контракты — `TypeContainers.cs`:

| Контракт | Что даёт |
|---|---|
| `ITypeContainer` | маркер |
| `IComponentContainer : ITypeContainer` | `ComponentType`, `TypeHashCode`, `Factory()`, `RegisterWorld(World)` |
| `ISystemContainer : ISystemSetter, ITypeContainer` | `SystemType`, `TypeHashCode`, `Factory()`, `BindSystem`/`UnBindSystem` |
| `IFastComponentContainer : ITypeContainer` | `RegisterWorld(World)`, `UnRegisterWorld(World)` |
| `IResolverContainer : ITypeContainer` | `RegisterResolvers(ResolversMap)` — объявлен в HECS.Serialize: ядро (оно же компилируется в генератор из форка HECSCore) не должно ссылаться на сериализацию |

Сбор — рукописный `internal static partial class TypeContainersRegistry` (`Providers/TypeContainersRegistry.cs`). Каждый сгенерированный файл контейнера дописывает partial-часть со строкой `private static readonly bool <X>Container = Add(new <X>Container());`; инициализаторы всех частей выполняются в статическом конструкторе, итог — `TypeContainersRegistry.All`. Два правила, ломать нельзя:

- у списка-накопителя **нет инициализатора поля** — инициализатор в части, скомпилированной позже, молча затрёт уже добавленные контейнеры;
- **явный статический конструктор обязателен** — без него тип `beforefieldinit`, и CoreCLR вправе не выполнить инициализаторы.

Дальше:

- `TypesProvider` (рукописный, `Providers/TypesProviderRegistration.cs`) разбирает `TypeContainersRegistry.All` по контрактам и в `Build()` назначает индексы и маски; совпадение `TypeHashCode` у двух типов → `InvalidOperationException` с именами обоих.
- `TypesMap` отдаёт `ComponentContainers`, `FastComponentContainers`, `ComponentsInfo`, `ComponentTypes`, `SystemTypes`, `GetContainers<T>()`.
- `World` в конструкторе: `foreach (var container in TypesMap.ComponentContainers) container.RegisterWorld(this);` → `ComponentProvider<T>.RegisterWorld(world)`. `FastWorld` — так же по `FastComponentContainers` → `FastComponentProvider<T>.RegisterWorld` / `UnRegisterWorld`.
- `ResolversMap` наполняется в `CollectRegistrations()` (генерируемый `ResolversMapRuntime.cs`) через `TypesMap.GetContainers<IResolverContainer>()`; контейнеры вызывают `internal` методы `RegisterResolver_<X>` / `RegisterCustom_<X>`.

Удалены: `ComponentProviderRegistrator<T>`, `TypeRegistrator<T>`, `partial void FillRegistrators()` / `FillTypeRegistrators()`, генерируемый `WorldRegistration.cs`.

## Fast Components

`IFastComponent` — **структурные** компоненты для «плотного» хранения. Генератор:

- собирает их в `Program.fastComponents` (по base-list структуры),
- регистрирует контейнером `Containers/<X>FastContainer.cs` (`IFastComponentContainer`),
- создаёт Unity-провайдер `<X>FastProvider : FastComponentMonoProvider<X>` в `FastComponentsProviders/`.

## Global Update

`GlobalUpdateSystem/` — планировщик апдейтов с модулями:

| Модуль | Когда |
|---|---|
| `UpdateModuleDefault` | обычный Update |
| `UpdateModuleFixed` | FixedUpdate |
| `UpdateModuleLate` | LateUpdate |
| `UpdateModuleAsync` | асинхронный тик |
| `UpdateModuleDeltaTime` | с дельтой времени |
| `UpdateModuleGlobalStart` | старт мира |
| `UpdateModuleReacts` | реактивные обновления |
| `PriorityUpdateModule` | приоритетные апдейты |
| `DispatchModule`, `JobSystem` | диспетчеризация и джобы |

Интерфейсы: `IUpdatable`, `IRegisterUpdate`, `ExecuteInUpdate`.

## Прикладные подсистемы

| Подсистема | Что даёт |
|---|---|
| `Abilities/` | Абилки: `BaseAbilitySystem`, `BaseAbilityNoPredicatesSystem`, `CompositeAbilitySystem`, `BasePassiveAbilitySystem`, `IAbility`, теги, команды `ExecuteAbility*`, `BaseAbilitiesHolderComponent`, `AbilitiesMap` |
| `Counters/` | Счётчики с модификаторами: `ModifiableFloat/IntCounter(Component)`, `SimpleFloat/IntCounterBaseComponent`, `CountersHolderComponent/System`, команды `AddCounterModifier`, `DiffCounter`, `ResetCounters` |
| `Modifiers/` | `IModifier`, `BaseFloatModifier`, `ModifiersContainer`, `ModifiersCalculation`, `OutModifiersFloatHolderComponent` |
| `Awaiters/` | Кастомные ожидания: `Awaiter`, `AsyncBuilder`, `JobsSystem`, `WaitForSeconds`, `WaitUntil`, `WaitWhile` |
| `Predicate.cs`, `Components/PredicatesComponent.cs` | `IPredicate` — условия (генерируются blueprint'ы) |
| `Rewards/` | `IReward`, `ExecuteReward` |
| `Animation/` | `AnimParametersMap`, `ScenarioAnimationComponent`, команды анимации |
| `Systems/PoolObject/`, `PoolingSystem` | Пулинг объектов |
| `DocumentationFeature/` | `[Documentation(Doc.X, "…")]`, `HECSDocumentation`, `DocHelper` |
| `Collections/`, `Helpers/` | `ConcurrencyList`, `ReactiveValue`, `ReadonlyList`, расширения сущностей/коллекций/векторов |

## Что должно совпадать между генератом и рантаймом

Меняя генератор, проверьте эти инварианты:

1. **Хеш типа.** `IndexGenerator.GetIndexForType(string)` — один алгоритм в генерате и в `BaseSystem.GetTypeHashCode`.
2. **Реестр контейнеров.** Имена `TypeContainersRegistry` и `Add` — генерат дописывает в него строку на контейнер.
3. **Контракты и точки входа.** `ITypeContainer`, `IComponentContainer`, `ISystemContainer`, `IFastComponentContainer`, `IResolverContainer`; статические `ComponentProvider<T>.RegisterWorld`, `FastComponentProvider<T>.RegisterWorld` / `UnRegisterWorld`; `ResolversMap.RegisterResolver_<X>` / `RegisterCustom_<X>`.
4. **Имена типов-обёрток.** `ComponentBluePrintContainer<T>`, `SystemBluePrint<T>`, `FastComponentMonoProvider<T>`, `ICommandResolver`, `ResolversMap`.
5. **Неймспейсы генерата.** `HECSFramework.Core` (ядро), `HECSFramework.Unity` (blueprints/провайдеры), `Components`, `Systems`, `Commands`.
6. **`order` в `[Field]`** — контракт бинарного формата, не переиспользуется.
