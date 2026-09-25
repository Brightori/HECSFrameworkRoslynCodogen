# Порт контейнерной схемы в боевое ядро

> **Статус (2026-09-25).** Регистрация контейнеров переведена с рефлексии на реестр; места ниже, помеченные ⟶, устарели.
> - Каждый файл контейнера дописывает строку в рукописный `internal static partial class TypeContainersRegistry` (`Providers/TypeContainersRegistry.cs`): `private static readonly bool <X>Container = Add(new <X>Container());`. У списка-накопителя нет инициализатора поля (иначе часть, скомпилированная позже, затрёт ранние регистрации), явный статический конструктор обязателен (иначе тип `beforefieldinit`, и CoreCLR может не выполнить инициализаторы). Методов `Register_*` / `WorldRegister_*` / `WorldRegisterFast_*` / `RegisterResolver_*`, собираемых рефлексией, `[Preserve]` и зарезервированных префиксов больше нет.
> - Контракты (`TypeContainers.cs`): маркер `ITypeContainer`; `IComponentContainer` — без `AfterBuild`, с `RegisterWorld(World)`; `ISystemContainer : ISystemSetter, ITypeContainer`; `IFastComponentContainer { RegisterWorld, UnRegisterWorld }`. `IResolverContainer { RegisterResolvers(ResolversMap) }` объявлен в HECS.Serialize — ядро (оно же компилируется в генератор) не ссылается на сериализацию.
> - `World`: `foreach (var container in TypesMap.ComponentContainers) container.RegisterWorld(this);` → `ComponentProvider<T>.RegisterWorld(world)`; `FastWorld` — так же по `FastComponentContainers` → `FastComponentProvider<T>.RegisterWorld/UnRegisterWorld`. Удалены `ComponentProviderRegistrator<T>`, `TypeRegistrator<T>`, `FillRegistrators()` / `FillTypeRegistrators()`, генерируемый `WorldRegistration.cs`.
> - `HECSMasks.cs` / `HMasks` не генерируются; `HMaskDummy`, `MaskProvider`, `IMaskProvider`, `TypesMap.MaskProvider` удалены из ядра. Дубликат `TypeHashCode` в `Build()` → `InvalidOperationException`.
> - Файлы: `Containers/<X>Container.cs`, `<X>FastContainer.cs`, `<X>ResolverContainer.cs`; старые `*.Container.cs` / `*.FastContainer.cs` / `*.CustomResolver.cs`, `HECSMasks.cs`, `WorldRegistration.cs` генератор удаляет при каждом прогоне.
> - Для порта к п.2 добавляются: `Providers/TypeContainersRegistry.cs`, контейнерные свойства `TypesMap` (`ComponentContainers`, `FastComponentContainers`, `ComponentsInfo`, `ComponentTypes`, `SystemTypes`, `GetContainers<T>()`), `RegisterWorld` в `ComponentProvider<T>` / `FastComponentProvider<T>`, циклы в `World` / `FastWorld`, удаление регистраторов, `HMaskDummy`, `MaskProvider`; `IResolverContainer` — в HECS.Serialize.

> Генератор переведён на контейнерный режим **по умолчанию** (флага нет).
> Сабмодуль HECSCore в этом репо старее боевого ядра (в нём нет
> ComponentProviderRegistrator / TypeRegistrator / World.FillRegistrators),
> поэтому часть правок нужно перенести в актуальное ядро руками. Этот файл — чеклист.

## 1. Что уже сделано в этом репозитории

| Файл | Что это |
|---|---|
| `RoslynHECS/HECSCore/TypeContainers.cs` | Контракты `IComponentContainer`, `ISystemContainer : ISystemSetter`, атрибут `Preserve` ⟶ атрибута больше нет, контракты — см. Статус |
| `RoslynHECS/HECSCore/Providers/TypesProviderRegistration.cs` | Ядро регистрации: конструктор со сбором `Register_*` рефлексией + `Build()` ⟶ конструктор читает `TypeContainersRegistry.All` |
| `RoslynHECS/HECSCore/TypesMap.cs` | Правка static-конструктора: `systemsSetters` берётся из провайдера |
| `RoslynHECS/ContainersGeneration.cs` | Новый генератор контейнеров (partial CodeGenerator) |
| `RoslynHECS/Program.cs` | SaveFiles на контейнерах, write-if-changed, `force_rebuild`, удаление легаси-монолитов |

## 2. Перенести в боевое ядро (обязательно)

1. **Скопировать** `TypeContainers.cs` и `TypesProviderRegistration.cs` в ядро.
2. **TypesMap**: в static-конструкторе добавить `systemsSetters = typeProvider.GetSystemContainers();` и **удалить легаси-партиалы** `SetSystemSetters` / `SetComponentsSetters` (вызовы и декларации) — поддержка старого генерата убрана полностью (см. правку в сабмодуле). ⚠ Следствие: старый сгенерированный `SystemBindings.cs` перестаёт компилироваться (его partial-реализация остаётся без декларации), поэтому прогнать генератор нужно ДО первой сборки после обновления ядра — он сам удалит легаси-файлы.
3. **Удалить/проверить**: старый генерируемый `TypeProvider.cs` содержал конструктор `TypesProvider` — теперь конструктор рукописный. Генератор сам удаляет легаси-файлы (`TypeProvider.cs`, `SystemBindings.cs`, `ComponentsWorldPart.cs`, `FastWorldPart.cs`, `MapResolver.cs`, `CustomAndUniversalResolvers.cs`) при первом прогоне.
4. **HECSFactory**: класс остаётся, но не используется — `TypesProvider` теперь сам реализует `IHECSFactory`. Можно выпилить позже.

## 3. World: партиал-методы теперь наполняются рефлексией

> ⟶ Заменено (см. Статус): партиал-методов, регистраторов и `WorldRegistration.cs` больше нет, `World` регистрирует провайдеры циклом по `TypesMap.ComponentContainers`.

Генерат пишет в каждый файл компонента метод `World.WorldRegister_<X>()` (и `WorldRegisterFast_<X>()` для фаст-компонентов), а стабильный файл `WorldRegistration.cs` реализует `FillRegistrators()` / `FillTypeRegistrators()`, собирая эти методы рефлексией.

Требования к боевому World (проверить, всё уже должно быть):

- поля `componentProviderRegistrators` (`ComponentProviderRegistrator[]`) и `typeRegistrators` (`TypeRegistrator[]`);
- партиал-декларации `partial void FillRegistrators();` и `partial void FillTypeRegistrators();` и их вызовы в инициализации мира.

Ничего рукописного добавлять в World не нужно — реализация приходит генератом.

## 4. ResolversMap: словари вместо switch-ей

`ResolversMapRuntime.cs` (генерируемый, стабильный — не зависит от набора типов) заменяет `MapResolver.cs`: тот же конструктор-порядок (`GetComponentContainerFunc`, `ProcessResolverContainer`, `GetComponentFromContainer`, затем `InitPartialCommandResolvers()`, `JSONModuleInit()`), но диспетчеризация — по словарям, наполняемым методами `RegisterResolver_<X>` / `RegisterCustom_<X>` из файлов контейнеров. ⟶ Методы `internal`, их вызывают контейнеры `IResolverContainer` из `CollectRegistrations()` (`TypesMap.GetContainers<IResolverContainer>()`), рефлексии нет.

Ожидания от рукописной части ResolversMap (как и раньше):

- поля-делегаты `GetComponentContainerFunc`, `ProcessResolverContainer`, `GetComponentFromContainer`;
- метод `PackComponentToContainer(...)`;
- поля `typeToCustomResolver`, `typeCodeToCustomResolver`, `getTypeIndexToType`;
- декларация `partial void InitPartialCommandResolvers();` (реализация — в CommandsMap.cs, как раньше);
- реализация `partial void JSONModuleInit()` — если есть.

`InitCustomResolvers` как partial-метод исчез — словари кастомных резолверов создаются в runtime-файле и наполняются регистрацией. Если рукописная часть его декларировала/вызывала — убрать.

## 5. ⚠ Ломающие изменения — проверить осознанно

1. **Ключи `[Union]` на IData сменились**: было — порядковый номер компонента, стало — `TypeHashCode` (стабильный, локальный, поэтому атрибуты можно раздать по файлам). Если `IData` где-то сериализуется полиморфно (юнионом) в сейвы/сеть — старые данные будут несовместимы. Разовая миграция.
2. **Индексы компонентов** теперь назначаются в рантайме по порядку регистрации (порядок методов из `GetMethods` — недетерминирован между версиями рантайма ⟶ теперь порядок инициализаторов partial-частей `TypeContainersRegistry`, его задаёт компилятор). Внутри процесса всё консистентно; наружу индексы уходить не должны (проверено по этому репо: наружу идут хеши и ShortID).
3. **Имена контейнеров** — `<TypeName>Container` (и `<TypeName>FastContainer`, `<TypeName>ResolverContainer`) в неймспейсе `HECSFramework.Core`: возможны коллизии с существующими классами (например, рукописный `HealthComponentContainer`). При коллизии — переименовать рукописный или поменять суффикс в `ContainersGeneration.ContainerSuffix` / `FastContainerSuffix` / `ResolverContainerSuffix`.
4. **[HECSResolver]-классы**: их резолверы теперь пишутся в `Resolvers/<X>Resolver.cs` — если класс с таким же именем есть среди компонентов, файлы столкнутся (раньше жили в одном CustomAndUniversalResolvers.cs).
5. **IL2CPP / managed stripping**: методы `Register_*`, `WorldRegister_*`, `RegisterResolver_*` вызываются только рефлексией. Генерат помечает их `[Preserve]` (атрибут из ядра — Unity уважает любой атрибут с именем PreserveAttribute). При High stripping level стоит дополнительно проверить, при проблемах — link.xml на `TypesProvider`, `World`, `ResolversMap` и `HECSGenerated`-сборку. ⟶ Заменено: рефлексии и `[Preserve]` нет, контейнеры достижимы статически через статический конструктор `TypeContainersRegistry`.

## 6. Порядок миграции проекта

1. Обновить ядро (п.2) — проект временно не соберётся (дублирование конструктора с легаси-генератом и осиротевший SystemBindings.cs), это ок: не собирать до шага 2.
2. Прогнать генератор: `RoslynHECS.exe path:<проект> force_rebuild` — удалит монолиты, зальёт контейнеры начисто.
3. Собрать, прогнать смоук: старт мира, создание энтити, биндинги систем, сериализация компонента туда-обратно, команды по сети (если есть).
4. Дальше — обычные прогоны без флагов: перезаписываются только изменённые файлы. ⟶ Удалённый/переименованный тип больше не требует `force_rebuild`: осиротевший контейнер удаляется тем же прогоном.

## 7. Что осталось как было

`HECSMasks.cs` (решение: пока не выпиливаем ⟶ выпилен, см. Статус), `CommandsMap.cs`/ShortID (ShortID теперь нумеруются после сортировки по имени с `StringComparer.Ordinal`), blueprint'ы, файлы резолверов компонентов (`Resolvers/*.cs`), FastComponentsProviders.
