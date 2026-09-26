# Архитектура

## Что это вообще такое

`RoslynHECS` — **внешний standalone-кодогенератор**. Он не MSBuild-таск, не Roslyn Source Generator и не Unity-скрипт. Это обычное консольное приложение, которому скармливают путь к папке с исходниками игры/сервера, а оно:

1. читает все `.cs` файлы как **текст**,
2. парсит их в синтаксические деревья Roslyn,
3. строит собственную объектную модель типов (наследование, partial-части, интерфейсы),
4. по этой модели пишет на диск готовые `.cs` файлы.

## Ключевое архитектурное решение: syntax-only, без семантики

Генератор работает **на уровне синтаксиса**, а не символов. `CSharpCompilation` создаётся (`Program.Compilation`), но `GetSemanticModel` используется точечно (в `LinkedNodeExtended`), и компиляция намеренно **не имеет ссылок на сборки** — `CSharpCompilation.Create("HelloWorld").AddSyntaxTrees(list)` и всё.

Последствия, которые определяют весь стиль кода:

| Следствие | Как это выглядит в коде |
|---|---|
| Типы сравниваются **по строке имени** | `x.BaseList.Types.Any(z => z.ToString() == "BaseComponent")` |
| Наследование строится **вручную** и рекурсивно | `ProcessLinkNodes` / `ProcessLinkNodesComponents` |
| Атрибуты читаются **по имени строки** | `attr.Name.ToString() == "Field"` |
| Неймспейсы для usings **угадываются** | серия перегрузок `GetNamespaces(...)` / `GetNameSpaceForCollection(...)` |
| Не нужен работающий билд целевого проекта | можно генерить по «сломанному» коду |

Плюс: скорость и независимость от Unity/сборки. Минус: чувствительность к переименованиям и алиасам (`using Comp = Components.X;` сломает матчинг).

## Слои

```
┌──────────────────────────────────────────────────────────┐
│  Program.cs — ОРКЕСТРАТОР                                │
│  args → чтение файлов → парсинг → визиторы → графы → save │
└───────────────────────┬──────────────────────────────────┘
                        │ статические коллекции Program.*
                        ▼
┌──────────────────────────────────────────────────────────┐
│  CodeGenerator (partial) — ГЕНЕРАТОРЫ                    │
│  CodogeneratorRoslynPart.cs / ContainersGeneration.cs /  │
│  FastWorldPart.cs                                        │
└───────────────────────┬──────────────────────────────────┘
                        │ строит дерево ISyntax
                        ▼
┌──────────────────────────────────────────────────────────┐
│  HECSCore/HECSGenerator/ — DSL ПОСТРОЕНИЯ ТЕКСТА         │
│  TreeSyntaxNode + TabSimpleSyntax + Left/RightScope…      │
│  .ToString() → готовый исходник                          │
└──────────────────────────────────────────────────────────┘
```

### Слой 1. Оркестратор (`Program`)

Хранит **глобальное состояние в статических полях**. Это сознательный приём: генераторы обращаются к `Program.componentOverData`, `Program.classes` и т.д. напрямую, без DI и передачи контекста.

Основные коллекции:

```csharp
List<ClassDeclarationSyntax>  classes, structs, interfaces          // всё, что нашли
Dictionary<string, LinkedNode> componentOverData                    // граф компонентов
Dictionary<string, LinkedNode> systemOverData                       // граф систем
Dictionary<string, LinkedInterfaceNode>        interfacesOverData   // граф интерфейсов
Dictionary<string, LinkedGenericInterfaceNode> genericInterfacesOverData // IReactCommand<T> и пр.
List<ClassDeclarationSyntax>  componentsDeclarations                // НЕабстрактные компоненты (blueprint'ы)
List<StructDeclarationSyntax> globalCommands, localCommands, networkCommands, fastComponents
Dictionary<string, ResolverData> customHecsResolvers
Dictionary<string, LinkedNode>   hecsResolverCollection
CSharpCompilation Compilation
```

> ⚠️ Порядок `componentsDeclarations` **ничего не задаёт** в рантайме: индекс компонента и `HECSMask.Index` назначает `TypesProvider.Build()` по порядку регистрации контейнеров, они процессно-локальны. Наружу (сейвы, сеть) уходят только `TypeHashCode` и ShortID. ShortID нумеруются в `GetShortIdPart` по имени типа (`StringComparer.Ordinal`): новый сетевой тип сдвигает номера, поэтому клиент и сервер генерируются с одинаковым набором сетевых типов.

### Слой 2. Модель типов

Три «узла», построенные поверх Roslyn-синтаксиса:

**`LinkedNode`** (объявлен в `Program.cs`) — класс (компонент или система):
- `Parent` — базовый узел (строится рекурсивно вниз от корней),
- `Parts` — все partial-части, включая себя,
- `Interfaces` — все интерфейсы, включая унаследованные,
- `IsAbstract` / `IsPartial` / `IsGeneric`,
- `GetParents()`, `GetInterfaces()`, `GetGenericInterfaces()`, `GetAllParentsAndParts()`.

**`LinkedInterfaceNode`** — интерфейс: `Parents`, `Parts`, `isPartial`, `isHaveReact` (эвристика: имя содержит `React`, флаг протягивается вверх по родителям).

**`LinkedGenericInterfaceNode`** — *конкретная инстанциация* дженерик-интерфейса, например `IReactCommand<DamageCommand>`: хранит `BaseInterface`, `GenericType` (`"DamageCommand"`), `MultiArguments`. Именно на этом строится биндинг систем.

**`LinkedNodeExtended`** (в `DataTypes/`) — «раскрытый» `LinkedNode` для сериализации: собирает все поля/свойства из parts и родителей в `MemberNode` → `GatheredField`, обрабатывает `[PartialSerializeField]`.

### Слой 3. Генераторы

Один `partial class CodeGenerator`, разложенный по трём файлам. Каждый публичный метод возвращает `string` (готовый файл) или `List<(name, content)>` (пачку файлов). Никаких side-effect'ов на диск — записывает только `Program.SaveFiles()`.

Регионы в `CodogeneratorRoslynPart.cs`:

```
#region SystemsBinding      → тела BindSystem/UnBindSystem для контейнеров систем (ProcessReacts)
#region Resolvers           → Resolvers/*.cs
#region CustomAndUniversalResolvers → тело универсального резолвера ([HECSResolver])
#region ...BluePrints...    → BluePrints (у компонентов и систем — со строкой регистрации в BluePrintsProvider)
#region CommandsResolvers   → CommandsMap.cs + ShortID
#region Documentation       → Documentation.cs (сейчас отключён)
```

Регионы в `ContainersGeneration.cs`:

```
#region ComponentContainer         → Containers/<X>Container.cs (компонент)
#region SystemContainer            → Containers/<X>Container.cs (система)
#region FastComponentContainer     → Containers/<X>FastContainer.cs
#region ResolversMapRuntime        → ResolversMapRuntime.cs
#region CustomResolverRegistration → Containers/<X>ResolverContainer.cs, Resolvers/<X>Resolver.cs ([HECSResolver])
```

Каждый файл контейнера дописывает partial-часть рукописного `TypeContainersRegistry` (ядро) одной строкой `private static readonly bool <X>Container = Add(new <X>Container());` — регистрация без рефлексии и без `[Preserve]`.

### Слой 4. DSL построения текста (`ISyntax`)

Вместо string-шаблонов — дерево узлов, где каждый узел умеет `ToString()`.

```csharp
var tree = new TreeSyntaxNode();
tree.Add(new UsingSyntax("Components"));
tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
tree.Add(new LeftScopeSyntax());
tree.Add(new TabSimpleSyntax(1, "public partial class World"));
tree.Add(new LeftScopeSyntax(1));
tree.Add(body);                      // body заполняется ПОСЛЕ добавления — работает по ссылке
tree.Add(new RightScopeSyntax(1));
tree.Add(new RightScopeSyntax());
return tree.ToString();
```

Два приёма, которые встречаются повсеместно:

1. **Отложенное заполнение.** Пустой `TreeSyntaxNode body` вставляется в нужное место дерева, а наполняется ниже по коду. Порядок вставки в дерево ≠ порядок наполнения.
2. **`AddUnique` / `AddUniqueSyntax`.** Дедупликация через `ToString().Contains(...)` — так собираются `using`-и без повторов. Дорого (O(n²) по строкам), но просто.

## Поток управления «сверху вниз»

```
Main(args)
 ├─ CheckArgs(args)                       — path / no_blueprints / no_resolvers / no_commands / server / defines
 ├─ GetFiles(*.cs, AllDirectories)        — исключая \Plugins, \HECSGenerated, \MessagePack
 ├─ Task.WhenAll(MakeTree)                — параллельный ParseText → ConcurrentBag<SyntaxTree>
 ├─ CSharpCompilation.Create("HelloWorld")
 ├─ ClassVirtualizationVisitor            — classes + classesByName
 │  StructVirtualizationVisitor           — structs
 │  InterfaceVirtualizationVisitor        — interfaces
 ├─ построение interfacesOverData         — + isHaveReact
 ├─ ProcessInterfaces()                   — родители интерфейсов + genericInterfacesOverData
 ├─ GatherSystems()                       — от BaseSystem/ISystem рекурсивно вниз
 ├─ GatherComponents()                    — от BaseComponent/IComponent рекурсивно вниз
 ├─ ProcessClasses()                      — componentsDeclarations + [HECSResolver]
 ├─ ProcessStructs(s) для каждой структуры — команды, IFastComponent, [HECSManualResolver]
 ├─ дозаполнение Interfaces у компонентов
 └─ SaveFiles()                           — вся генерация и запись на диск
```

Детальный разбор каждого шага — в [CODEGEN_PIPELINE.md](CODEGEN_PIPELINE.md).

## Границы ответственности и точки расширения

| Хочу изменить… | Иду в… |
|---|---|
| набор входных файлов / фильтры | `Program.Main` (фильтр `.Where(...)`) |
| аргументы командной строки | `Program.CheckArgs` |
| какие типы считаются компонентом/системой | `Program.GatherComponents` / `GatherSystems` |
| какие структуры считаются командой | `Program.ProcessStructs` |
| что именно записывается на диск | `Program.SaveFiles` |
| содержимое конкретного файла | соответствующий метод `CodeGenerator` |
| синтаксические примитивы | `HECSCore/HECSGenerator/SyntaxTree.cs` (сабмодуль!) |

## Известные архитектурные слабости

Полезно знать до того, как что-то сломается:

- **Хардкод путей по умолчанию** — `Program.ScriptsPath` / `HECSGenerated` указывают на локальную машину автора. Без аргумента `path:` генератор пойдёт по несуществующему пути.
- **`commandMapneeded` инициализируется в `false`**, а `CheckArgs` при **пустом** `args` делает ранний `return`. Значит без аргументов `CommandsMap.cs` не генерируется, а с любым аргументом (кроме `no_commands`) — генерируется. Флаги `resolversNeeded`/`bluePrintsNeeded` инициализированы `true`, поэтому у них поведение обратное.
- **`Containers/`, `Resolvers/`, `FastComponentsProviders/` очищаются целиком** (`CleanDirectory`) при `force_rebuild`. Ручные правки там будут потеряны; без флага перезаписываются только изменённые файлы, а осиротевшие (файлы типов, которых больше нет) удаляются каждый прогон.
- **`SaveToFile` глотает исключения** — пишет `"we cant save file to ..."` в консоль и продолжает. Частичная генерация может пройти «успешно».
- **Индекс компонента зависит от порядка регистрации**, а порядок инициализаторов partial-частей `TypeContainersRegistry` задаёт компилятор. Индексы и маски поэтому не переносимы между сборками — и не должны уходить наружу: стабильный якорь — `TypeHashCode` (совпадение хешей двух типов `Build()` отвергает `InvalidOperationException`).
- **Матчинг по строкам** ломается на алиасах и полностью квалифицированных именах в base-list.
- Генератор `Documentation` (`GetDocumentationRoslyn`) жив, но из `SaveFiles` не вызывается. Генераторов `MaskProvider` / `ComponentContext` больше нет.
