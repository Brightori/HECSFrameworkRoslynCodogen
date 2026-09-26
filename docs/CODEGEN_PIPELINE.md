# Пайплайн кодогенерации

Пошаговый разбор того, что происходит от запуска `.exe` до файлов на диске.

---

## 0. Запуск и аргументы

```bash
RoslynHECS.exe path:D:\MyProject\Assets\
RoslynHECS.exe path:/repo/Server/ server no_blueprints
RoslynHECS.exe path:D:\MyProject\Assets\ no_resolvers no_commands
```

`Program.CheckArgs(string[] args)`:

| Аргумент | Эффект |
|---|---|
| `path:<путь>` | `ScriptsPath = <путь>`, нормализуется через `Path.GetFullPath` + завершающий разделитель. Задаёт и `HECSGenerated` |
| `server` | `HECSGenerated = <path>/HECSGenerated/` (без `Scripts`) — для серверного репозитория |
| *(без `server`)* | `HECSGenerated = <path>/Scripts/HECSGenerated/` — раскладка Unity |
| `no_blueprints` | `bluePrintsNeeded = false` |
| `no_resolvers` | `resolversNeeded = false` |
| `no_commands` | `commandMapneeded = false` |
| `defines:A;B` | `parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(...)`, разделители `;` и `,` |
| `force_rebuild` | `forceRebuild = true`: очистка директорий генерата и запись без сверки с диском (§8) |

> ⚠️ **Ловушка.** При `args.Length == 0` метод делает ранний `return` до присвоения флагов. Поля остаются в своих инициализаторах: `resolversNeeded = true`, `bluePrintsNeeded = true`, **`commandMapneeded = false`**. То есть «запуск без аргументов» ≠ «запуск с `path:` без остальных флагов»: во втором случае `CommandsMap.cs` будет сгенерирован.

> ⚠️ Без `path:` используются **захардкоженные** пути из `Program.cs`:
> `ScriptsPath = @"D:\Develop\StalkerSurviviorGitLab\Assets\"`.

---

## 1. Сбор и парсинг исходников

```csharp
files = new DirectoryInfo(ScriptsPath)
    .GetFiles("*.cs", SearchOption.AllDirectories)
    .Where(x => !x.FullName.Contains("\\Plugins")
             && !x.FullName.Contains("\\HECSGenerated")
             && !x.FullName.Contains("\\MessagePack"))
    .ToList();
```

Исключаются: `Plugins` (сторонний код), `HECSGenerated` (**чтобы не жрать собственный выхлоп**), `MessagePack` (генерат сериализатора).

> Фильтр использует `\\` — это **Windows-специфично**. На macOS/Linux исключения не сработают.

Каждый файл читается асинхронно и парсится: `CSharpSyntaxTree.ParseText(text, parseOptions)` → `ConcurrentBag<SyntaxTree>`. Попутно запоминается существующий `CommandsMap.cs` (`alrdyHaveCommandMap`) — чтобы позже перезаписать его **на месте**, а не создавать дубль.

> Символов препроцессора по умолчанию нет: всё под `#if` — disabled trivia, типы и поля оттуда в генерат не попадают и никак себя не проявляют. Символы включаются аргументом `defines:`, но осознанно: новый видимый сетевой тип сдвигает ShortID, а клиент и сервер должны генерироваться с одинаковым набором.

Затем:

```csharp
Compilation = CSharpCompilation.Create("HelloWorld").AddSyntaxTrees(list);
```

Компиляция без ссылок на сборки — нужна почти исключительно для `GetSemanticModel` в `LinkedNodeExtended.ProcessPartialSerializationFields`.

---

## 2. Визиторы: плоские списки типов

Три `CSharpSyntaxRewriter`:

| Визитор | Собирает | Побочный эффект |
|---|---|---|
| `ClassVirtualizationVisitor` | `List<ClassDeclarationSyntax> Classes` | заполняет `Program.classesByName` (первое вхождение по имени) |
| `StructVirtualizationVisitor` | `List<StructDeclarationSyntax> Structs` | — |
| `InterfaceVirtualizationVisitor` | `List<InterfaceDeclarationSyntax> Interfaces` | — |

Обратите внимание: список `Classes` содержит **каждую partial-часть отдельной записью**. Дедупликация происходит позже, на уровне `LinkedNode.Parts`.

---

## 3. Граф интерфейсов

Для каждого интерфейса создаётся `LinkedInterfaceNode`:

```csharp
isPartial   = модификатор "partial"
Parts       = все объявления с тем же именем (если partial)
isHaveReact = Name.Contains("React")     // эвристика!
```

`ProcessInterfaces()`:
1. для каждого узла разбирает base-list (у всех parts, если partial) → заполняет `Parents`; флаг `isHaveReact` протягивается от родителя к потомку;
2. проходит по **всем классам** и для каждого типа в их base-list вызывает `ProcessGenericInterface` — так в `genericInterfacesOverData` попадают **закрытые** дженерик-интерфейсы вида `IReactCommand<DamageCommand>` с полями `GenericType = "DamageCommand"`, `MultiArguments`.

Дженерик-интерфейсы, унаследованные через промежуточные интерфейсы, тоже подхватываются (обход `interfaceCache`).

---

## 4. Граф систем — `GatherSystems()`

**Корни:** классы, у которых в base-list есть строка `BaseSystem` или `ISystem` (и сам класс не называется `BaseSystem`).

Для каждого создаётся `LinkedNode`, затем `ProcessLinkNodes` **рекурсивно вниз**: ищет классы, наследующие от текущего (по строковому имени; для дженериков — по `GenericNameSyntax.Identifier`), создаёт им узлы, проставляет `Parent` и уходит глубже.

Результат: `Program.systemOverData` — плоский словарь `имя → LinkedNode`, где связи родитель/потомок восстановлены.

---

## 5. Граф компонентов — `GatherComponents()`

Ровно та же схема, корни — base-list содержит `BaseComponent` или `IComponent`. Результат — `Program.componentOverData`.

Отличие: `Parts` компонента всегда включает собственное объявление, а интерфейсы собираются с каждой partial-части.

---

## 6. `ProcessClasses()`

Две вещи:

1. **`componentsDeclarations`** — все **неабстрактные** компоненты (идут в blueprint'ы). Порядок в списке на индексы не влияет — их назначает рантайм (§10).
2. Классы с атрибутом `[HECSResolver]` → `hecsResolverCollection` + запись в `customHecsResolvers` (`имя → имяResolver`).

---

## 7. `ProcessStructs(s)` — команды, fast-компоненты, ручные резолверы

Для каждой структуры смотрим base-list по строкам:

| Найдено в base-list | Куда попадает |
|---|---|
| `IGlobalCommand` | `globalCommands` **и** `localCommands` |
| `ICommand` | `localCommands` |
| `INetworkCommand` / `INetworkLocalCommand` | `globalCommands` + `localCommands` + `networkCommands` |
| `IFastComponent` | `fastComponents` |

Плюс атрибут `[HECSManualResolver(typeof(T))]` на структуре → `customHecsResolvers[T] = { ResolverName = имя структуры }`. Поддерживаются и пользовательские типы (`IdentifierNameSyntax`), и встроенные (`PredefinedTypeSyntax`, например `typeof(int)`).

---

## 8. `SaveFiles()` — что и куда пишется

Файлы копятся в памяти и пишутся в конце параллельно (`FlushFiles`); файл, совпадающий с диском (без учёта переводов строк), не перезаписывается.

Перед генерацией `SaveFiles` удаляет легаси-файлы: монолиты `TypeProvider.cs`, `SystemBindings.cs`, `ComponentsWorldPart.cs`, `FastWorldPart.cs`, `MapResolver.cs`, `CustomAndUniversalResolvers.cs`, `HECSMasks.cs`, `WorldRegistration.cs` и контейнеры со старыми именами `Containers/*.Container.cs`, `*.FastContainer.cs`, `*.CustomResolver.cs` (`DeleteLegacyContainers`).

> 🧹 Каждый прогон после записи из `Containers/`, `Resolvers/`, `FastComponentsProviders/` удаляются все `.cs` (с `.meta`), которых этот прогон не породил; в прогоне с `no_resolvers` чистится только `Containers/`, а `*FastContainer.cs` / `*ResolverContainer.cs` не трогаются (`DeleteOrphans`, счётчик «удалено осиротевших» в итоговой строке). При `force_rebuild` эти директории **полностью очищаются** (`CleanDirectory`) и пишутся без сверки.

### 8.1 Всегда

Файл на тип, имя файла = имя класса контейнера (без точек):

| Файл | Метод | Что внутри |
|---|---|---|
| `HECSGenerated/Containers/<X>Container.cs` (компонент) | `GetComponentContainer()` | строка регистрации в `TypeContainersRegistry` + `sealed class <X>Container : IComponentContainer` (`ComponentType`, `TypeHashCode`, `Factory()`, `RegisterWorld(world)` → `ComponentProvider<X>.RegisterWorld(world)`). При `resolversNeeded` ещё `IResolverContainer` (`RegisterResolvers(map)` → `map.RegisterResolver_<X>()`), partial `ResolversMap` с `internal void RegisterResolver_<X>()` и `[Union(TypeHashCode, …)]` на `IData` |
| `HECSGenerated/Containers/<X>Container.cs` (система) | `GetSystemContainerFile()` | строка регистрации + `sealed class <X>Container : ISystemContainer` с `BindSystem`/`UnBindSystem` (см. §9) |

Строка регистрации — partial-часть рукописного `TypeContainersRegistry` (ядро):

```csharp
internal static partial class TypeContainersRegistry
{
    private static readonly bool HealthComponentContainer = Add(new HealthComponentContainer());
}
```

`Documentation.cs` (`GetDocumentationRoslyn`) в `SaveFiles` не вызывается (в комментарии автора: *«не получается нормально автоматизировать, слишком сложные параметры у атрибута»*). Генераторов `MaskProvider.cs` / `ComponentContext.cs` больше нет.

### 8.2 Если `resolversNeeded`

| Файл | Метод |
|---|---|
| `HECSGenerated/ResolversMapRuntime.cs` | `GetResolversMapRuntime()` — стабильная инфраструктура `ResolversMap`: словари по хешам; `CollectRegistrations()` = `foreach (var container in TypesMap.GetContainers<IResolverContainer>()) container.RegisterResolvers(this);` |
| `HECSGenerated/Resolvers/<X>Resolver.cs` | `GetSerializationResolvers()`; для `[HECSResolver]`-классов — `GetUniversalResolverFile()` |
| `HECSGenerated/Containers/<X>FastContainer.cs` | `GetFastComponentContainer()` — строка регистрации + `IFastComponentContainer` (`RegisterWorld`/`UnRegisterWorld` → `FastComponentProvider<X>`) для каждого `IFastComponent` |
| `HECSGenerated/Containers/<X>ResolverContainer.cs` | `GetCustomResolverRegistration()` — строка регистрации + `IResolverContainer` и `internal void RegisterCustom_<X>()` в `ResolversMap` |
| `HECSGenerated/FastComponentsProviders/<X>FastProvider.cs` | `GetProvidersForFastComponent()` |

### 8.3 Если `commandMapneeded`

`CommandsMap.cs` ← `GenerateNetworkCommandsAndShortIdsMap(networkCommands)`.
Если такой файл был найден при сканировании — перезаписывается **по своему исходному пути**; иначе кладётся в `HECSGenerated/`.

Внутри: `partial class ResolversMap` с `Dictionary<int, ICommandResolver> Map`, `Dictionary<Type, int> CommandsIDs`, ShortID-часть и `InitPartialCommandResolvers()`.

### 8.4 Если `bluePrintsNeeded`

Пишутся **не** в `HECSGenerated`, а в дерево исходников проекта:

| Куда | Что | Критерий отбора |
|---|---|---|
| `<Scripts>/BluePrints/ComponentsBluePrints/` | `<X>BluePrint.cs : ComponentBluePrintContainer<X>` + строка `BluePrintsProvider.RegisterComponent` | все `componentsDeclarations` |
| `<Scripts>/BluePrints/SystemsBluePrint/` | `<X>BluePrint.cs : SystemBluePrint<X>` + строка `BluePrintsProvider.RegisterSystem` | все неабстрактные системы |
| `<Scripts>/BluePrints/PredicatesBlueprints/` | `<X>Blueprint.cs` | неабстрактный класс с `IPredicate` в base-list |
| `<Scripts>/BluePrints/Actions/` | `<X>Blueprint.cs` | класс с `IAction` **или** `IAsyncAction` в base-list |

Пути blueprint'ов склеиваются как `ScriptsPath + "/Scripts/BluePrints/..."` — то есть привязаны к Unity-раскладке.

---

## 9. Как устроены контейнеры систем (`Containers/<X>Container.cs`)

Самый нетривиальный генератор (бывший `SystemBindings.cs`). Для каждой **неабстрактной** системы создаётся контейнер с методами bind/unbind (`FillSystemBindings`). Наполняется из двух источников:

### 9.1 Дженерик-интерфейсы (`ProcessReacts`)

Берутся **все** дженерик-интерфейсы системы, включая унаследованные от родителей и partial-частей (`GetGenericInterfaces`).

| Интерфейс | Генерируемый bind |
|---|---|
| `IReactCommand<T>` | `LocalCommandListener<T>.AddListener(world.Index, currentSystem)` |
| `IReactGlobalCommand<T>` | `system.Owner.World.AddGlobalReactCommand<T>(system, currentSystem)` |
| `IRequestProvider<T>` | `World.AddRequestProvider<T>(currentSystem)` |
| `IRequestProvider<T1,T2>` | `World.AddRequestProvider<T1,T2>(currentSystem)` |

Только при `CommandMapNeeded`:

| Интерфейс | Генерируемый bind |
|---|---|
| `IReactNetworkCommandGlobal<T>` | `GlobalNetworkCommandListener<T>.AddListener(...)` |
| `IReactNetworkCommandLocal<T>` | `LocalNetworkCommandListener<T>.AddListener(...)` |
| `IRequestProcessor<T>` / `<T1,T2>` | `RegisterRequestProcessor<...>` с ветвлением `#if SERVER` (`Server.ServerHelpers.GetSingleSystemFromServer<DataSenderSystem>`) / `#else` (`EntityManager.GetSingleSystem<DataSenderSystem>`) |

Закомментированы (не генерируются сейчас): `IReactComponentLocal`, `IReactComponentGlobal`, `IReactGenericLocalComponent`, `IReactGenericGlobalComponent`.

Отдельный костыль: если система наследуется от **дженерик-родителя** и `GenericType == "T"`, тип подменяется реальным аргументом из base-list потомка.

### 9.2 Атрибуты полей (`[Required]`, `[Single]`)

Обходятся все parts и родители системы (`GetAllParentsAndParts`), внутри — `AttributeListSyntax`:

| Атрибут | private/protected поле | public поле |
|---|---|---|
| `[Required]` | `SetPrivateComponentBinder` — генерируется поле-мост в контейнере | `SetPublicComponentBinder` — прямое присваивание |
| `[Single]` | `SetPrivateSingleComponentBinder` | `SetPublicSingleComponentBinder` |

В конце, если тело bind непустое, наверх добавляется каст:

```csharp
var currentSystem = (MySystem)system;
```

---

## 10. Индексы, маски и хеши

### TypeHashCode

`IndexGenerator.GenerateIndex(string typeName)` — детерминированный самописный хеш имени типа:

```csharp
int index = length + typeName[0].GetHashCode() + typeName[^1].GetHashCode();
int hash  = 10070531;
for (int i = 0; i < length; i++) {
    int charC = typeName[i].GetHashCode() * i;
    index += charC + 101161 * (i + 3) + (hash ^ (charC * i));
}
```

Тот же метод живёт в рантайме (`BaseSystem.GetTypeHashCode`) — поэтому **алгоритм менять нельзя**, иначе разъедутся генерат и рантайм.

### Индекс и маска компонента

Генератор их больше не пишет (`HECSMasks.cs` / `HMasks` не генерируются). `HECSMask` — пара `Index` + `TypeHashCode`; `Index` назначает рукописный `TypesProvider.Build()` в рантайме, в порядке регистрации контейнеров (`index = i + 1`, `0` занят `DefaultEmpty`). Индекс процессно-локален: наружу уходят только `TypeHashCode` и ShortID. Два типа с одинаковым `TypeHashCode` → `Build()` бросает `InvalidOperationException` с именами обоих.

### ShortID

`GetShortIdPart()` нумерует сетевые команды и `INetworkComponent` подряд после сортировки по имени типа (`OrderBy(x => x.Type, StringComparer.Ordinal).ToList()`) — не зависит от культуры. Новый сетевой тип сдвигает номера следующих за ним по алфавиту.

---

## 11. Резолверы сериализации

`GetSerializationResolvers()` идёт по всем неабстрактным компонентам:

- если у компонента (или любой его partial-части) есть `[HECSDefaultResolver]` → компонент попадает в `containersSolve`, но **собственный резолвер не генерируется** (используется дефолтный);
- иначе → и в `containersSolve`, и в `needResolver` → генерируется `<X>Resolver.cs`.

Внутри `GetResolver` создаётся `LinkedNodeExtended`, который собирает поля со всех parts и родителей.

### Что представляет собой резолвер

```csharp
[MessagePackObject, Serializable]
public partial struct HealthComponentResolver
    : IResolver<HealthComponent>, IResolver<HealthComponentResolver, HealthComponent>, IData
{
    [Key(0)] public float Current;
    public HealthComponentResolver In(ref HealthComponent healthcomponent) { … return this; }
    public void Out(ref Entity entity) { … }
    public void Out(ref HealthComponent healthcomponent) { … }
}
```

Атрибуты MessagePack (`[MessagePackObject]`, `[Key(order)]`) проставляются автоматически — `order` из `[Field]` становится ключом.

### Какие поля сериализуются

| Способ | Где объявляется | Что делает |
|---|---|---|
| `[Field(order)]` / `[Field(order, typeof(CustomResolver))]` | на поле/свойстве | включает член в резолвер. Работает и для приватных членов |
| `[PartialSerializeField(order, "fieldName")]` / `(order, "fieldName", "resolverName")` | **на классе** | помечает уже существующее поле сериализуемым «снаружи» (`IsPartial = true`) |

Дальше важна классификация **public / private**, её даёт `MemberNode.IsPublic()`:

- **поле** — публично, если в модификаторах есть `public`;
- **свойство** — публично, если у него есть `AccessorList` с сеттером, и сеттер не помечен `private`/`protected`.

| Категория | Как генерируется |
|---|---|
| Публичные | прямое присваивание `this.Field = component.Field;` в `In()` и обратно в `Out()` |
| Приватные (`IsPrivateFieldIncluded`) | дополнительно генерируется partial-класс компонента с методами `Save(ref resolver)` / `Load(ref resolver)` (`GetPartialClassForSerializePrivateFields`), а `In()`/`Out()` вызывают их |

> ⚠️ Отдельный метод `IsValidField` (он **требует** `public`) применяется **не здесь**, а только в ветке универсальных резолверов `[HECSResolver]` (`GetUniversalResolver`). Не путайте два пути.

### Спецслучаи

| Условие | Эффект |
|---|---|
| компонент реализует `IBeforeSerializationComponent` | в `In()` добавляется вызов `component.BeforeSync();` |
| компонент реализует `IAfterSerializationComponent` | в `Out()` добавляется вызов `component.AfterSync();` |
| свойство типа `ReactiveValue<T>` | сериализуется `.CurrentValue`, а не сам объект |
| у поля указан `ResolverName` | вместо типа поля в резолвер кладётся тип-резолвер, вызывается `new XResolver().In(ref field)` / `.Out(ref field)` |

`order` — порядковый номер поля в бинарном формате (`[Key]` MessagePack). **Не переиспользуйте освободившиеся номера** — это ломает совместимость сохранений и сетевых пакетов.

---

## 12. Быстрая шпаргалка по атрибутам

| Атрибут | На чём | Что делает |
|---|---|---|
| `[Field(order[, typeof(Resolver)])]` | поле/свойство | включает член в сериализацию (`[Key(order)]`); приватные поддерживаются |
| `[PartialSerializeField(order, "name"[, "resolver"])]` | класс | пометить существующее поле сериализуемым, не трогая его объявление |
| `[HECSDefaultResolver]` | компонент | не генерировать собственный резолвер |
| `[HECSResolver]` | класс | сгенерировать универсальный резолвер `<X>Resolver` |
| `[HECSManualResolver(typeof(T))]` | структура | зарегистрировать структуру как ручной резолвер типа `T` |
| `[Required]` | поле системы | авто-биндинг компонента владельца |
| `[Single]` | поле системы | авто-биндинг single-компонента/системы мира |
| `[Documentation(Doc.X, "…")]` | тип/член | документация (генератор сейчас отключён) |

---

## 13. Диагностика

Генератор пишет в консоль:

```
Путь: …                              — ScriptsPath
Путь кодогена: …                     — HECSGenerated
Найдены аргументы запуска: …
<число>                              — сколько .cs файлов найдено
нашли глобальную команду <Имя>
нашли локальную команду <Имя>
components <N>
systems<N>
успешно сохранено
we cant save file to <путь>          — ⚠️ ошибка записи, НЕ прерывает работу
```

Если «успешно сохранено» напечатано, а файлов нет — ищите строки `we cant save file to` выше.
