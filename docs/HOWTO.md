# HOWTO — рецепты работы с генератором

Практические сценарии: от «просто запустить» до «добавить новый генерируемый файл».

---

## Сборка и запуск

### Собрать

```bash
dotnet build RoslynHECS.sln -c Debug
```

Проект: `net7.0`, `Exe`, `AllowUnsafeBlocks`.
Публикация настроена на self-contained single-file `win-x64` (`PublishSingleFile`, `PublishTrimmed`, `PublishReadyToRun`) — есть профили под Windows и macOS в `RoslynHECS/Properties/PublishProfiles/`.

> `global.json` фиксирует SDK `3.1`, хотя проект таргетит `net7.0`. Если сборка падает на выборе SDK — обновите `global.json` или удалите его.

### Клонировать с сабмодулем

```bash
git clone --recurse-submodules git@github.com:Brightori/HECSFrameworkRoslynCodogen.git
# или, если уже склонировано:
git submodule update --init --recursive
```

Без `HECSCore` проект **не соберётся** — там лежит `ISyntax`/`TreeSyntaxNode`.

### Опубликовать

```bash
dotnet publish RoslynHECS/RoslynHECS.csproj -c Debug -p:PublishProfile=WindowsProfile
```

### Запустить

```bash
# Unity-проект
RoslynHECS.exe path:D:\MyGame\Assets\

# Серверный проект (генерат в <path>/HECSGenerated/)
RoslynHECS.exe path:/repo/Server/ server

# Только контейнеры типов, без резолверов и blueprint'ов
RoslynHECS.exe path:D:\MyGame\Assets\ no_resolvers no_blueprints
```

Из IDE — профиль `RoslynHECS` в `Properties/launchSettings.json`; аргументы там не заданы, поэтому **запуск из VS пойдёт по хардкоду** `D:\Develop\StalkerSurviviorGitLab\Assets\`. Добавьте `commandLineArgs` под себя.

---

## Рецепт 1. Переключить генератор на свой проект

Не редактируйте `Program.ScriptsPath` — передавайте `path:`.

Если всё же нужно поменять дефолт:

```csharp
// Program.cs
public static string ScriptsPath   = @"D:\Develop\StalkerSurviviorGitLab\Assets\";
public static string HECSGenerated = @"D:\Develop\StalkerSurviviorGitLab\Assets\Scripts\HECSGenerated\";
```

Требования к целевому проекту:
- исходники ядра HECS должны быть **внутри** `ScriptsPath` (генератор ищет `BaseComponent`, `ISystem` и т.д. в разобранных файлах);
- папки `Plugins`, `HECSGenerated`, `MessagePack` исключаются — по подстроке с `\`, то есть **фильтр работает только на Windows**.

---

## Рецепт 2. Добавить новый компонент/систему в игре

Ничего в генераторе менять не надо:

```csharp
// Components/HealthComponent.cs
namespace Components
{
    [Serializable]
    public partial class HealthComponent : BaseComponent
    {
        [Field(0)] public float Current;
        [Field(1)] public float Max;
    }
}
```

Запустить генератор → появятся `Containers/HealthComponentContainer.cs`, `HealthComponentResolver.cs`, `HealthComponentBluePrint.cs`.

> Индексы и маски компонентов назначаются в рантайме и наружу не уходят — новый компонент сетевой протокол не ломает. Исключение — сетевые типы (`INetworkComponent`, сетевые команды): новый сдвигает ShortID, клиент и сервер генерируются с одинаковым набором.

---

## Рецепт 3. Подписать систему на команду

```csharp
public sealed class DamageSystem : BaseSystem, IReactCommand<DamageCommand>
{
    public override void InitSystem() { }
    public void CommandReact(DamageCommand command) { /* … */ }
}
```

Генератор увидит `IReactCommand<DamageCommand>` в base-list и допишет в `BindSystem` контейнера `Containers/DamageSystemContainer.cs`:

```csharp
var currentSystem = (DamageSystem)system;
LocalCommandListener<DamageCommand>.AddListener(currentSystem.Owner.World.Index, currentSystem);
```

Для глобальной — `IReactGlobalCommand<T>`. Для сетевых — `IReactNetworkCommandLocal<T>` / `IReactNetworkCommandGlobal<T>` **и запуск без `no_commands`**.

Работает и через наследование: если интерфейс объявлен на базовой системе или в другой partial-части — он всё равно будет найден (`GetGenericInterfaces` обходит родителей и parts).

---

## Рецепт 4. Автобиндинг компонента в систему

```csharp
public sealed class MoveSystem : BaseSystem
{
    [Required] public TransformComponent Transform;     // публичное — присваивается напрямую
    [Required] private SpeedComponent speed;            // приватное — через сгенерированное поле-мост
    [Single]   public GameSettingsComponent Settings;   // синглтон мира

    public override void InitSystem() { }
}
```

Генератор разложит это на bind/unbind в контейнере системы. Атрибуты ищутся во **всех** parts и родителях.

---

## Рецепт 5. Управлять сериализацией

**Обычное поле:**

```csharp
[Field(0)] public int Level;
```

`order` становится `[Key(0)]` в MessagePack-резолвере.

**Приватное поле.** Два равноценных способа — `[Field]` прямо на приватном поле или `[PartialSerializeField]` на классе:

```csharp
public partial class SecretComponent : BaseComponent
{
    [Field(1)] private int hidden;          // способ 1
}

[PartialSerializeField(2, "another")]        // способ 2 — не трогая само поле
public partial class SecretComponent : BaseComponent
{
    private int another;
}
```

В обоих случаях компонент должен быть `partial` — генератор допишет ему partial-часть с `Save(ref resolver)` / `Load(ref resolver)`.

> Свойство считается публичным только если у него есть сеттер и он не `private`/`protected`.

**Хуки до/после сериализации:** реализуйте `IBeforeSerializationComponent` (`BeforeSync()`) или `IAfterSerializationComponent` (`AfterSync()`) — вызовы попадут в `In()`/`Out()` автоматически. Свойства типа `ReactiveValue<T>` сериализуются через `.CurrentValue`.

**Свой резолвер для поля:**

```csharp
[Field(3, typeof(Vector3Resolver))] public Vector3 Position;
```

**Отключить генерацию резолвера для компонента:**

```csharp
[HECSDefaultResolver]
public partial class SomeComponent : BaseComponent { }
```

**Ручной резолвер произвольного типа:**

```csharp
[HECSManualResolver(typeof(Quaternion))]
public struct QuaternionResolver { /* … */ }
```

**Универсальный резолвер класса:**

```csharp
[HECSResolver]
public partial class SaveData { }   // → SaveDataResolver
```

> 🔢 `order` — номер поля в бинарном формате. Не меняйте и не переиспользуйте номера удалённых полей, иначе поедут сейвы и сетевые пакеты.

---

## Рецепт 6. Добавить новый генерируемый файл

Три шага.

**1. Написать метод генерации** (в `CodogeneratorRoslynPart.cs` или в новом файле с `public partial class CodeGenerator`):

```csharp
public string GetMyThing()
{
    var tree = new TreeSyntaxNode();
    var body = new TreeSyntaxNode();          // отложенное заполнение

    tree.Add(new UsingSyntax("System.Collections.Generic", 1));
    tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
    tree.Add(new LeftScopeSyntax());
    tree.Add(new TabSimpleSyntax(1, "public static partial class MyThing"));
    tree.Add(new LeftScopeSyntax(1));
    tree.Add(body);
    tree.Add(new RightScopeSyntax(1));
    tree.Add(new RightScopeSyntax());

    foreach (var c in Program.componentsDeclarations)
        body.Add(new TabSimpleSyntax(2, $"public const string {c.Identifier.ValueText} = \"{c.Identifier.ValueText}\";"));

    return tree.ToString();
}
```

**2. Зарегистрировать константу имени файла** в `Program.cs`:

```csharp
private const string MyThing = "MyThing.cs";
```

**3. Вызвать из `SaveFiles()`:**

```csharp
SaveToFile(MyThing, processGeneration.GetMyThing(), HECSGenerated);
```

### Шпаргалка по `ISyntax`

| Узел | Даёт |
|---|---|
| `new TreeSyntaxNode()` | контейнер, склеивает детей |
| `new UsingSyntax("X")` / `new UsingSyntax("X", 1)` | `using X;` (второй аргумент — **дополнительные** переводы строки после; по умолчанию один) |
| `new NameSpaceSyntax("X")` | `namespace X` |
| `new LeftScopeSyntax(n)` / `new RightScopeSyntax(n)` | `{` / `}` с отступом `n` |
| `new RightScopeSyntax(n, true)` / `(n, false)` | `};` / `},` — закрытие инициализатора или элемента списка |
| `new TabSimpleSyntax(n, "текст")` | строка с отступом `n` |
| `new ParagraphSyntax()` | пустая строка |
| `new CompositeSyntax(a, b, c)` | склейка узлов в строку |
| `new SimpleSyntax("текст")` / `new TabSpaceSyntax(n)` | сырой текст / только табы |
| `syntax.AddUnique(other)` | добавить без дублей (для usings) |
| `GetDictionaryHelper.GetDictionaryMethod(...)` | заготовка метода-фабрики словаря |

---

## Рецепт 7. Расширить набор распознаваемых типов

**Новый вид команды** → `Program.ProcessStructs`:

```csharp
if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains("IMyCommand")))
    myCommands.AddOrRemoveElement(s, true);
```

**Новый вид системы/компонента** → `GatherSystems` / `GatherComponents`, строка отбора корней:

```csharp
var pureSystems = classes.Where(x => x.Identifier.ValueText != "BaseSystem"
    && x.BaseList != null
    && x.BaseList.Types.Any(z => z.ToString() == "BaseSystem" || z.ToString() == "ISystem"));
```

**Новый react-интерфейс для биндинга** → `CodogeneratorRoslynPart.ProcessReacts`: добавьте `const string` наверху класса и `case` в `switch (part.BaseInterface.Name)`.

---

## Рецепт 8. Отладка генерации

1. **Ничего не сгенерировалось.** Проверьте вывод: число найденных файлов, `components N`, `systems N`. Ноль → неправильный `ScriptsPath` или не сработал матчинг base-list.
2. **Файлы «сохранены», но их нет.** Ищите в консоли `we cant save file to …` — `SaveToFile` глотает исключения.
3. **У компонента нет контейнера.** Он абстрактный? Наследуется от `BaseComponent` по цепочке, где какое-то звено вне `ScriptsPath`? Имя базового типа в base-list написано с неймспейсом или через алиас?
4. **Поле не сериализуется.** `[Field]` без аргументов (`ArgumentList == null` → пропуск)? Свойство без сеттера? Компонент помечен `[HECSDefaultResolver]` (тогда свой резолвер не генерируется)? Компонент абстрактный?
5. **Интерфейс-реакция не подхватился.** Он дженерик? Только дженерик-интерфейсы попадают в `genericInterfacesOverData`. Проверьте, что имя интерфейса точно совпадает с константой в `CodeGenerator`.
6. **Смотреть промежуточное состояние** удобнее всего брейкпойнтом в `Program.SaveFiles()` — там уже собраны все графы. Или временный дамп:
   ```csharp
   File.WriteAllLines("dump.txt", Program.componentsDeclarations.Select((c, i) => $"{i}: {c.Identifier.ValueText}"));
   ```

---

## Рецепт 9. Работа с сабмодулем HECSCore

`HECSCore` — отдельный репозиторий. Изменения `SyntaxTree.cs`/`CParse.cs` затрагивают **все** проекты на HECS.

```bash
cd RoslynHECS/HECSCore
git checkout main && git pull
# правки, коммит, пуш в HECSFrameworkCore
cd ../..
git add RoslynHECS/HECSCore    # фиксируем новый указатель сабмодуля
git commit -m "bump HECSCore"
```

---

## Чек-лист перед коммитом изменений в генератор

- [ ] Собирается `dotnet build`
- [ ] Прогнан на реальном проекте, в консоли нет `we cant save file to`
- [ ] Числа `components N` / `systems N` не изменились неожиданно
- [ ] Целевой проект компилируется с новым генератом
- [ ] Не менялись: алгоритм `IndexGenerator`, `TypeContainersRegistry`/`Add`, контракты контейнеров (`I*Container`), `ComponentProvider<T>.RegisterWorld`, `FastComponentProvider<T>.RegisterWorld`/`UnRegisterWorld`
- [ ] `order` существующих `[Field]` не переиспользованы
- [ ] Если правился `HECSCore` — сабмодуль запушен и указатель обновлён
