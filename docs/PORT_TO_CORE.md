# Порт контейнерной схемы в боевое ядро

> Генератор переведён на контейнерный режим **по умолчанию** (флага нет).
> Сабмодуль HECSCore в этом репо старее боевого ядра (в нём нет
> ComponentProviderRegistrator / TypeRegistrator / World.FillRegistrators),
> поэтому часть правок нужно перенести в актуальное ядро руками. Этот файл — чеклист.

## 1. Что уже сделано в этом репозитории

| Файл | Что это |
|---|---|
| `RoslynHECS/HECSCore/TypeContainers.cs` | Контракты `IComponentContainer`, `ISystemContainer : ISystemSetter`, атрибут `Preserve` |
| `RoslynHECS/HECSCore/Providers/TypesProviderRegistration.cs` | Ядро регистрации: конструктор со сбором `Register_*` рефлексией + `Build()` |
| `RoslynHECS/HECSCore/TypesMap.cs` | Правка static-конструктора: `systemsSetters` берётся из провайдера |
| `RoslynHECS/ContainersGeneration.cs` | Новый генератор контейнеров (partial CodeGenerator) |
| `RoslynHECS/Program.cs` | SaveFiles на контейнерах, write-if-changed, `force_rebuild`, удаление легаси-монолитов |

## 2. Перенести в боевое ядро (обязательно)

1. **Скопировать** `TypeContainers.cs` и `TypesProviderRegistration.cs` в ядро.
2. **TypesMap**: в static-конструкторе добавить `systemsSetters = typeProvider.GetSystemContainers();` и **удалить легаси-партиалы** `SetSystemSetters` / `SetComponentsSetters` (вызовы и декларации) — поддержка старого генерата убрана полностью (см. правку в сабмодуле). ⚠ Следствие: старый сгенерированный `SystemBindings.cs` перестаёт компилироваться (его partial-реализация остаётся без декларации), поэтому прогнать генератор нужно ДО первой сборки после обновления ядра — он сам удалит легаси-файлы.
3. **Удалить/проверить**: старый генерируемый `TypeProvider.cs` содержал конструктор `TypesProvider` — теперь конструктор рукописный. Генератор сам удаляет легаси-файлы (`TypeProvider.cs`, `SystemBindings.cs`, `ComponentsWorldPart.cs`, `FastWorldPart.cs`, `MapResolver.cs`, `CustomAndUniversalResolvers.cs`) при первом прогоне.
4. **HECSFactory**: класс остаётся, но не используется — `TypesProvider` теперь сам реализует `IHECSFactory`. Можно выпилить позже.

## 3. World: партиал-методы теперь наполняются рефлексией

Генерат пишет в каждый файл компонента метод `World.WorldRegister_<X>()` (и `WorldRegisterFast_<X>()` для фаст-компонентов), а стабильный файл `WorldRegistration.cs` реализует `FillRegistrators()` / `FillTypeRegistrators()`, собирая эти методы рефлексией.

Требования к боевому World (проверить, всё уже должно быть):

- поля `componentProviderRegistrators` (`ComponentProviderRegistrator[]`) и `typeRegistrators` (`TypeRegistrator[]`);
- партиал-декларации `partial void FillRegistrators();` и `partial void FillTypeRegistrators();` и их вызовы в инициализации мира.

Ничего рукописного добавлять в World не нужно — реализация приходит генератом.

## 4. ResolversMap: словари вместо switch-ей

`ResolversMapRuntime.cs` (генерируемый, стабильный — не зависит от набора типов) заменяет `MapResolver.cs`: тот же конструктор-порядок (`GetComponentContainerFunc`, `ProcessResolverContainer`, `GetComponentFromContainer`, затем `InitPartialCommandResolvers()`, `JSONModuleInit()`), но диспетчеризация — по словарям, наполняемым методами `RegisterResolver_<X>` / `RegisterCustom_<X>` из файлов контейнеров.

Ожидания от рукописной части ResolversMap (как и раньше):

- поля-делегаты `GetComponentContainerFunc`, `ProcessResolverContainer`, `GetComponentFromContainer`;
- метод `PackComponentToContainer(...)`;
- поля `typeToCustomResolver`, `typeCodeToCustomResolver`, `getTypeIndexToType`;
- декларация `partial void InitPartialCommandResolvers();` (реализация — в CommandsMap.cs, как раньше);
- реализация `partial void JSONModuleInit()` — если есть.

`InitCustomResolvers` как partial-метод исчез — словари кастомных резолверов создаются в runtime-файле и наполняются регистрацией. Если рукописная часть его декларировала/вызывала — убрать.

## 5. ⚠ Ломающие изменения — проверить осознанно

1. **Ключи `[Union]` на IData сменились**: было — порядковый номер компонента, стало — `TypeHashCode` (стабильный, локальный, поэтому атрибуты можно раздать по файлам). Если `IData` где-то сериализуется полиморфно (юнионом) в сейвы/сеть — старые данные будут несовместимы. Разовая миграция.
2. **Индексы компонентов** теперь назначаются в рантайме по порядку регистрации (порядок методов из `GetMethods` — недетерминирован между версиями рантайма). Внутри процесса всё консистентно; наружу индексы уходить не должны (проверено по этому репо: наружу идут хеши и ShortID).
3. **Имена контейнеров** — `<TypeName>Container` в неймспейсе `HECSFramework.Core`: возможны коллизии с существующими классами (например, рукописный `HealthComponentContainer`). При коллизии — переименовать рукописный или поменять суффикс в `ContainersGeneration.ContainerSuffix`.
4. **[HECSResolver]-классы**: их резолверы теперь пишутся в `Resolvers/<X>Resolver.cs` — если класс с таким же именем есть среди компонентов, файлы столкнутся (раньше жили в одном CustomAndUniversalResolvers.cs).
5. **IL2CPP / managed stripping**: методы `Register_*`, `WorldRegister_*`, `RegisterResolver_*` вызываются только рефлексией. Генерат помечает их `[Preserve]` (атрибут из ядра — Unity уважает любой атрибут с именем PreserveAttribute). При High stripping level стоит дополнительно проверить, при проблемах — link.xml на `TypesProvider`, `World`, `ResolversMap` и `HECSGenerated`-сборку.

## 6. Порядок миграции проекта

1. Обновить ядро (п.2) — проект временно не соберётся (дублирование конструктора с легаси-генератом и осиротевший SystemBindings.cs), это ок: не собирать до шага 2.
2. Прогнать генератор: `RoslynHECS.exe path:<проект> force_rebuild` — удалит монолиты, зальёт контейнеры начисто.
3. Собрать, прогнать смоук: старт мира, создание энтити, биндинги систем, сериализация компонента туда-обратно, команды по сети (если есть).
4. Дальше — обычные прогоны без флагов: перезаписываются только изменённые файлы. Удалил/переименовал тип → ошибка компиляции в осиротевшем контейнере → прогон с `force_rebuild`.

## 7. Что осталось как было

`HECSMasks.cs` (решение: пока не выпиливаем), `CommandsMap.cs`/ShortID, blueprint'ы, файлы резолверов компонентов (`Resolvers/*.cs`), FastComponentsProviders.
