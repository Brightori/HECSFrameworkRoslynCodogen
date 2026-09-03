# Спека: раздельные контейнеры типов вместо монолитного генерата

> Статус: согласовано в обсуждении, к имплементации.
> Связанные документы: [ARCHITECTURE.md](ARCHITECTURE.md), [CODEGEN_PIPELINE.md](CODEGEN_PIPELINE.md).

## 1. Цель

Сейчас генератор производит монолитные файлы (`TypeProvider.cs`, `SystemBindings.cs`, `HECSMasks.cs`, `ComponentsWorldPart.cs`, `FastWorldPart.cs`), каждый из которых зависит от **полного списка** типов проекта. Любое изменение любого компонента/системы перезаписывает монолиты целиком и вызывает каскадную рекомпиляцию.

Целевое состояние:

- **один изолированный файл на тип** (компонент или система), содержащий только локальные данные типа;
- файл меняется **только когда меняется сам тип** (или его зависимости — родители/интерфейсы);
- **ноль глобальных генерируемых файлов** — ни агрегатора, ни словарей-литералов;
- всё глобальное (индексы, маски, Count, карты) вычисляется в рантайме на старте.

## 2. Ключевые решения (и почему)

| Решение | Обоснование |
|---|---|
| Регистрация — методы `Register_*` в partial-частях самого `TypesProvider`, сбор рефлексией по префиксу | Нет агрегатора; файл типа полностью самодостаточен; сбор по одному известному типу — дешевейшая рефлексия; pull-модель без гонок инициализации (в отличие от `[ModuleInitializer]`) |
| Индексы и маски назначаются в `Build()` по **порядку регистрации, без сортировки** | Порядок компонентов процессно-локален и наружу не уходит; стабильность нужна только сетевым командам (ShortID — отдельный механизм, не затронут) |
| Два раздельных контракта `IComponentContainer` / `ISystemContainer` | Нет мёртвых методов; `Build()` не разбирает «кто есть кто» |
| Маркер-параметр у `Register_*` НЕ используется | Уникальные имена + префикс достаточны; префикс `Register_` зарезервирован внутри `TypesProvider` |
| `HMasks` — кандидат на полное удаление | Маска становится внутренней деталью провайдера; см. §9 «Открытые вопросы» |

Отвергнутые альтернативы: тонкий генерируемый агрегатор (лишний глобальный файл), `[ModuleInitializer]` (гонка: module initializer может не выполниться к моменту `Build()`, если сборка генерата ещё не тронута), `[RuntimeInitializeOnLoadMethod]` (Unity-only, серверу нужен второй путь), полный рефлексивный скан типов (дорого, метаданные), assembly-атрибуты (работает, но регистрация разъезжается с контейнером и требует атрибут в ядре), статические конструкторы с общим предком (ленивые, касание предка не каскадирует на потомков — CLR не умеет).

## 3. Контракты (рукописные, HECSCore)

```csharp
public interface IComponentContainer
{
    Type ComponentType { get; }
    int TypeHashCode { get; }              // IndexGenerator по имени — константа в генерате
    IComponent Factory();
    void InjectResolvers(ResolversMap map); // вызывается ядром условно (конфигурация resolvers)
    void AfterBuild(HECSMask mask, int index); // обратная связь: назначенные index/mask
}

public interface ISystemContainer
{
    Type SystemType { get; }
    int TypeHashCode { get; }
    ISystem Factory();
    void Bind(ISystem system);             // тела из бывшего SystemBindings.cs
    void UnBind(ISystem system);
}
```

При отказе от `HMasks` метод `AfterBuild` сжимается (или исчезает — если регистрацию провайдера компонента ядро делает само по данным контейнера).

## 4. Ядро `TypesProvider` (рукописное, стабильное)

```csharp
public partial class TypesProvider
{
    private readonly List<IComponentContainer> componentContainers = new(512);
    private readonly List<ISystemContainer> systemContainers = new(256);
    private readonly Dictionary<int, IComponentContainer> componentsByHash = new(512);
    private readonly Dictionary<int, ISystemContainer> systemsByHash = new(256);

    public void RegisterComponent(IComponentContainer c) => componentContainers.Add(c);
    public void RegisterSystem(ISystemContainer s) => systemContainers.Add(s);

    public TypesProvider()
    {
        var methods = typeof(TypesProvider).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var m in methods)
            if (m.Name.StartsWith("Register_", StringComparison.Ordinal))
                m.Invoke(this, null);

        Build();
    }

    private void Build()
    {
        Count = componentContainers.Count + 1;
        var maskFieldCount = (componentContainers.Count + 60) / 61;    // бывший ComponentsCountRoslyn

        MapIndexes = new Dictionary<int, ComponentMaskAndIndex>(Count)
        {
            { -1, new ComponentMaskAndIndex { ComponentName = "DefaultEmpty", ComponentsMask = HECSMask.Empty } }
        };

        for (int i = 0; i < componentContainers.Count; i++)
        {
            var c = componentContainers[i];
            var mask = BuildMask(i, maskFieldCount, c.TypeHashCode);   // бывший CalculateIndexesForMaskRoslyn

            MapIndexes.Add(c.TypeHashCode, new ComponentMaskAndIndex
                { ComponentName = c.ComponentType.Name, ComponentsMask = mask });
            TypeToComponentIndex.Add(c.ComponentType, i);
            TypeToHash.Add(c.ComponentType, c.TypeHashCode);
            HashToType.Add(c.TypeHashCode, c.ComponentType);
            componentsByHash.Add(c.TypeHashCode, c);

            c.AfterBuild(mask, i);
        }

        foreach (var s in systemContainers)
            systemsByHash.Add(s.TypeHashCode, s);

        if (resolversEnabled)
            foreach (var c in componentContainers)
                c.InjectResolvers(ResolversMap);
    }

    // API (замена HECSFactory и SystemBindings):
    public IComponent GetComponentFromFactory(int hash) => componentsByHash[hash].Factory();
    public ISystem GetSystemFromFactory(int hash) => systemsByHash[hash].Factory();
    public void BindSystem(ISystem system) => systemsByHash[system.GetTypeHashCode].Bind(system);
    public void UnBindSystem(ISystem system) => systemsByHash[system.GetTypeHashCode].UnBind(system);
}
```

`Build()` — **единственное** место в системе, где существуют «индекс» и «бит маски». В файлах на диске эти числа больше не встречаются — это и делает генерат инкрементально-стабильным.

## 5. Шаблоны генерируемых файлов

### Компонент — `HECSGenerated/Containers/<X>.Container.cs`

```csharp
public partial class TypesProvider
{
    [Preserve]
    private void Register_HealthComponent()
        => RegisterComponent(new HealthComponentContainer());
}

public sealed class HealthComponentContainer : IComponentContainer
{
    public Type ComponentType => typeof(HealthComponent);
    public int TypeHashCode => 123456789;                   // IndexGenerator.GenerateIndex("HealthComponent")
    public IComponent Factory() => new HealthComponent();

    public void InjectResolvers(ResolversMap map)
        => map.Register<HealthComponent, HealthComponentResolver>();

    public void AfterBuild(HECSMask mask, int index) { /* см. §9: HMasks / провайдер */ }
}
```

Допустимая компактная форма для простых компонентов — дженерик-контейнер на делегатах в ядре (`ComponentContainer<T>`), тогда файл состоит из одного метода `Register_*`. Системам с большими Bind-телами — явный sealed-класс.

### Система — `HECSGenerated/Containers/<X>.Container.cs`

```csharp
public partial class TypesProvider
{
    [Preserve]
    private void Register_DamageSystem()
        => RegisterSystem(new DamageSystemContainer());
}

public sealed class DamageSystemContainer : ISystemContainer
{
    public Type SystemType => typeof(DamageSystem);
    public int TypeHashCode => 987654321;
    public ISystem Factory() => new DamageSystem();

    public void Bind(ISystem system)
    {
        var currentSystem = (DamageSystem)system;
        // ProcessReacts: IReactCommand<T> / IReactGlobalCommand<T> / IRequestProvider<...>
        LocalCommandListener<DamageCommand>.AddListener(currentSystem.Owner.World.Index, currentSystem);
        // [Required]/[Single] биндинги полей
    }

    public void UnBind(ISystem system) { /* зеркально */ }
}
```

`[Preserve]` на каждом `Register_*` обязателен: под IL2CPP managed stripping вырезает методы без статических путей вызова. Альтернатива — запись в `link.xml` на `TypesProvider` целиком.

## 6. Что умирает

| Файл генерата | Куда делось содержимое |
|---|---|
| `TypeProvider.cs` (словари-литералы, `HECSFactory`) | `Build()` + диспетчеризация в контейнеры |
| `SystemBindings.cs` | `Bind`/`UnBind` контейнеров систем |
| `ComponentsWorldPart.cs` (`FillRegistrators`) | `AfterBuild` / ядро по данным контейнеров |
| `FastWorldPart.cs` (`FillTypeRegistrators`) | контейнеры fast-компонентов (тот же паттерн) |
| `HECSMasks.cs` | вероятно удаляется целиком (§9) |

Не затронуто: резолверы (`Resolvers/*.cs` — уже пофайловые; меняется только регистрация — теперь через `InjectResolvers`), blueprint'ы, `CommandsMap.cs`/ShortID (сетевой контракт, отдельный механизм).

## 7. План правок

### HECSCore (сабмодуль — затрагивает все проекты на HECS!)

1. `IComponentContainer`, `ISystemContainer` (+ опционально `ComponentContainer<T>` на делегатах).
2. `TypesProvider`: конструктор со сбором `Register_*`, `Build()`, `BuildMask`, API `BindSystem`/`UnBindSystem`/фабрики. Убрать ожидание генерируемого конструктора.
3. `World`: `FillRegistrators`/`FillTypeRegistrators` заменить на получение данных от провайдера.
4. Решение по `HMasks` (§9).

### RoslynHECS (генератор)

1. Новый генератор `GetTypeContainer(LinkedNode)` — шаблон из §5; переиспользует: `ProcessReacts` (тела Bind), логику `[Required]`/`[Single]`, `IndexGenerator` для хеша.
2. `SaveFiles()`: вместо пяти монолитов — цикл по `componentOverData`/`systemOverData` → `Containers/<X>.Container.cs`.
3. Выпилить вызовы `GenerateTypesMapRoslyn`, `GetSystemBindsByRoslyn`, `GenerateHecsMasksRoslyn`, `GetEntitiesWorldPart`, `GetFastWorldPart` (код можно оставить на переходный период под флагом).
4. **Diff-sync обязателен**: контейнер удалённого типа ссылается на несуществующий тип → ошибка компиляции. Генератор при каждом прогоне удаляет `Containers/*.Container.cs` для типов, которых больше нет.
5. Write-if-changed: перед записью сравнивать содержимое, не перезаписывать неизменённое.

### Порядок миграции

1. Правки HECSCore (контракты + ядро) с сохранением старого пути (генерируемый конструктор) под `#if` или партиал-заглушкой.
2. Генератор: новый режим (флаг `containers`), старый остаётся дефолтом.
3. Прогон на реальном проекте, сверка поведения (число типов, биндинги, сериализация).
4. Переключение дефолта, удаление старых генераторов и старых файлов генерата.

## 8. Ограничения и инварианты

- **Одна сборка.** Partial-части `TypesProvider` обязаны компилироваться в одну сборку с ядром (сейчас так и есть — генерат уже дописывает `partial class TypesProvider`/`World`). Разнос генерата по asmdef потребует смены механизма (якорь-класс на сборку или assembly-атрибуты).
- **Префикс `Register_` зарезервирован** внутри `TypesProvider` — рукописные методы ядра его не используют.
- **Уникальность имён типов** — как и сейчас, весь генератор оперирует bare-именами; одноимённые типы в разных неймспейсах не поддерживаются.
- `IndexGenerator` и `TypeHashCode` — без изменений; наружу (сеть/сейвы) уходят только хеши и ShortID, не индексы.
- Стоимость старта: `GetMethods` + ~N×`Invoke` один раз ≈ доли мс (.NET 7) … единицы мс (IL2CPP на ~800 типах). Приемлемо.

## 9. Открытые вопросы

1. **Судьба `HMasks`.** Решение — скорее всего удалить целиком (маска — внутренняя деталь провайдера). Перед удалением проверить колл-сайты быстрого пути `TryGetHecsComponent<T>(HECSMask, out T)` в игровых проектах: если быстрый доступ уже идёт через static generic провайдеры — рудимент; если маски дергаются руками — нужен проход с заменой на дженерик-API.
2. **Форма контейнера компонентов**: sealed-класс всегда vs делегатная форма для простых случаев.
3. **Инкрементальная генерация** (перегенерация только изменённых типов, манифест зависимостей) — отдельная спека, следующий этап.
