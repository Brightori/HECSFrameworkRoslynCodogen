using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HECSFramework.Core;
using HECSFramework.Core.Generator;
using HECSFramework.Core.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS.DataTypes;
using RoslynHECS.Helpers;
using static HECSFramework.Core.Generator.CodeGenerator;
using ClassDeclarationSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace RoslynHECS
{
    class Program
    {
        public static List<string> components = new List<string>(2048);
        public static List<ClassDeclarationSyntax> componentsDeclarations = new List<ClassDeclarationSyntax>(2048);
        public static List<ClassDeclarationSyntax> allComponentsDeclarations = new List<ClassDeclarationSyntax>(2048);
        public static List<ClassDeclarationSyntax> partialDeclarations = new List<ClassDeclarationSyntax>(2048);
        public static List<StructDeclarationSyntax> globalCommands = new List<StructDeclarationSyntax>(2048);
        public static List<StructDeclarationSyntax> fastComponents = new List<StructDeclarationSyntax>(2048);
        public static List<StructDeclarationSyntax> localCommands = new List<StructDeclarationSyntax>(2048);
        public static List<StructDeclarationSyntax> networkCommands = new List<StructDeclarationSyntax>(2048);

        //resolvers collection
        public static Dictionary<string, ResolverData> customHecsResolvers = new Dictionary<string, ResolverData>(256);
        public static Dictionary<string, LinkedNode> hecsResolverCollection = new Dictionary<string, LinkedNode>(512);

        public static Dictionary<string, StructDeclarationSyntax> structByName = new Dictionary<string, StructDeclarationSyntax>(4000);
        public static Dictionary<string, ClassDeclarationSyntax> classesByName = new Dictionary<string, ClassDeclarationSyntax>(4000);
        public static Dictionary<string, InterfaceDeclarationSyntax> allInterfacesByName = new Dictionary<string, InterfaceDeclarationSyntax>(1024);
        public static Dictionary<string, LinkedNode> systemOverData = new Dictionary<string, LinkedNode>(512);
        public static Dictionary<string, LinkedNode> componentOverData = new Dictionary<string, LinkedNode>(512);
        public static Dictionary<string, LinkedInterfaceNode> interfacesOverData = new Dictionary<string, LinkedInterfaceNode>(512);
        public static Dictionary<string, LinkedGenericInterfaceNode> genericInterfacesOverData = new Dictionary<string, LinkedGenericInterfaceNode>(512);

        public static List<ClassDeclarationSyntax> classes;
        public static List<StructDeclarationSyntax> structs;
        public static List<InterfaceDeclarationSyntax> interfaces;

        //индексы типов, строятся один раз после визиторов (BuildTypeIndexes):
        //все объявления по имени (включая partial-части) и дети по базовому типу
        public static Dictionary<string, List<ClassDeclarationSyntax>> classDeclarationsByName = new Dictionary<string, List<ClassDeclarationSyntax>>(4000);
        public static Dictionary<string, List<StructDeclarationSyntax>> structDeclarationsByName = new Dictionary<string, List<StructDeclarationSyntax>>(4000);
        public static Dictionary<string, List<InterfaceDeclarationSyntax>> interfaceDeclarationsByName = new Dictionary<string, List<InterfaceDeclarationSyntax>>(1024);
        //ключ: базовый тип строкой как он записан в BaseList ("BaseComponent", "Foo<int>")
        public static Dictionary<string, List<ClassDeclarationSyntax>> childrenByBase = new Dictionary<string, List<ClassDeclarationSyntax>>(4000);
        //ключ: идентификатор generic-базы ("Foo" для ": Foo<int>")
        public static Dictionary<string, List<ClassDeclarationSyntax>> childrenByGenericBase = new Dictionary<string, List<ClassDeclarationSyntax>>(512);
        //неймспейсы по имени типа (классы, структуры, интерфейсы, enum'ы) — единый источник using в генерате
        public static Dictionary<string, List<string>> namespacesByTypeName = new Dictionary<string, List<string>>(4000);

        public static string ScriptsPath = @"D:\Develop\StalkerSurviviorGitLab\Assets\";
        public static string HECSGenerated = @"D:\Develop\StalkerSurviviorGitLab\Assets\Scripts\HECSGenerated\";
        //public static string ScriptsPath = @"E:\repos\Kefir\minilife-server\MinilifeServer\";
        //public static string HECSGenerated = @"E:\repos\Kefir\minilife-server\MinilifeServer\HECSGenerated\";

        private const string HecsMasks = "HECSMasks.cs";
        private const string BluePrintsProvider = "BluePrintsProvider.cs";
        private const string CommandsMap = "CommandsMap.cs";

        //имена легаси-монолитов: сами генераторы удалены, имена нужны только для зачистки
        private const string TypeProvider = "TypeProvider.cs";
        private const string SystemBindings = "SystemBindings.cs";
        private const string MapResolver = "MapResolver.cs";
        private const string CustomAndUniversalResolvers = "CustomAndUniversalResolvers.cs";

        private const string ComponentsBluePrintsPath = "/Scripts/BluePrints/ComponentsBluePrints/";
        private const string SystemsBluePrintsPath = "/Scripts/BluePrints/SystemsBluePrint/";
        private const string PredicatesBlueprints = "/Scripts/BluePrints/PredicatesBlueprints/";
        private const string ActionsBlueprints = "/Scripts/BluePrints/Actions/";

        private const string BaseComponent = "BaseComponent";
        private const string HECSManualResolver = "HECSManualResolver";
        private const string HECSResolver = "HECSResolver";

        private static bool resolversNeeded = true;
        private static bool bluePrintsNeeded = true;
        private static bool commandMapneeded = false;
        private static bool forceRebuild = false;

        private static int savedFilesCount = 0;
        private static int skippedFilesCount = 0;
        private static int failedFilesCount = 0;

        //SaveToFile только копит файлы, на диск они уходят параллельно в FlushFiles
        private static readonly List<(string path, string data)> pendingFiles = new List<(string path, string data)>(2048);

        public static bool CommandMapNeeded => commandMapneeded;

        private static HashSet<LinkedInterfaceNode> interfaceCache = new HashSet<LinkedInterfaceNode>(32);
        private static List<FileInfo> files;

        private static FileInfo alrdyHaveCommandMap;
        public static CSharpCompilation Compilation;

        //код под #if без объявленного символа уходит в disabled trivia - типы и поля
        //оттуда молча не попадают в генерат. По умолчанию символов нет: включение
        //дефайна добавляет типы в componentsDeclarations и сдвигает биты масок,
        //поэтому это осознанный аргумент запуска, а не поведение по умолчанию
        private static CSharpParseOptions parseOptions = CSharpParseOptions.Default;

        static async Task Main(string[] args)
        {
            CheckArgs(args);

            Console.WriteLine($"Путь: {ScriptsPath}");
            Console.WriteLine($"Путь кодогена: {HECSGenerated}");
            Console.WriteLine($"Найдены аргументы запуска: {string.Join(", ", args)}");
            Console.WriteLine($"Доступные аргументы: {Environment.NewLine}{string.Join(Environment.NewLine, new[] { "path:путь_до_скриптов", "no_blueprints", "no_resolvers", "no_commands", "server", "force_rebuild", "defines:СИМВОЛ1;СИМВОЛ2" })}");

            var test = Directory.GetDirectories(ScriptsPath);
            var phaseTimer = System.Diagnostics.Stopwatch.StartNew();

            //var files = new DirectoryInfo(ScriptsPath).GetFiles("*.cs", SearchOption.AllDirectories);
            files = new DirectoryInfo(ScriptsPath).GetFiles("*.cs", SearchOption.AllDirectories).Where(x => !x.FullName.Contains("\\Plugins") && !x.FullName.Contains("\\HECSGenerated") && !x.FullName.Contains("\\MessagePack")).ToList();
            Console.WriteLine(files.Count);

            //порядок файлов фиксируем: от него зависит порядок классов, а значит содержимое
            //монолитов (HECSMasks, BluePrintsProvider); ConcurrentBag давал случайный порядок
            //и эти файлы переписывались на каждом прогоне
            files.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));

            var tasks = new List<Task<SyntaxTree>>(files.Count);

            foreach (var f in files)
            {
                if (f.Extension == ".cs")
                {
                    tasks.Add(MakeTree(f));
                }
            }

            var list = await Task.WhenAll(tasks);
            Console.WriteLine($"парсинг: {phaseTimer.ElapsedMilliseconds}ms");
            phaseTimer.Restart();

            //foreach (var f in files)
            //{
            //    if (f.Extension == ".cs")
            //    {
            //        var s = File.ReadAllText(f.FullName);
            //        var syntaxTree = CSharpSyntaxTree.ParseText(s);
            //        list.Add(syntaxTree);

            //        if (f.Name == CommandsMap)
            //            alrdyHaveCommandMap = f;
            //    }
            //}

            Compilation = CSharpCompilation.Create("HelloWorld").AddSyntaxTrees(list);

            CollectTypeDeclarations(list);
            Console.WriteLine($"сбор типов: {phaseTimer.ElapsedMilliseconds}ms");
            phaseTimer.Restart();

            BuildTypeIndexes();

            foreach (var i in interfaces)
            {
                var name = i.Identifier.ValueText;

                if (interfacesOverData.ContainsKey(name)) continue;

                var node = new LinkedInterfaceNode
                {
                    Name = name,
                    InterfaceDeclaration = i,
                    Parents = new HashSet<LinkedInterfaceNode>(8),
                    Parts = new HashSet<InterfaceDeclarationSyntax>(8),
                    isPartial = i.Modifiers.Any(x => x.ToString() == "partial"),
                };
                interfacesOverData.Add(name, node);

                if (node.isPartial)
                {
                    node.Parts = interfaceDeclarationsByName[name].ToHashSet();
                }

                node.isHaveReact = name.Contains("React");
            }

            ProcessInterfaces();
            GatherSystems();
            GatherComponents();

            ProcessClasses();

            foreach (var s in structs)
                ProcessStructs(s);

            foreach (var c in componentOverData.Values)
            {
                var newInterfaces = new HashSet<LinkedInterfaceNode>();
                c.GetInterfaces(newInterfaces);
                c.Interfaces = newInterfaces;
            }

            Console.WriteLine($"графы типов: {phaseTimer.ElapsedMilliseconds}ms");
            Console.WriteLine("components " + componentOverData.Count);
            Console.WriteLine("systems" + systemOverData.Count);

            SaveFiles();
            Console.WriteLine("успешно сохранено");
            //Thread.Sleep(1500);
        }

        /// <summary>
        /// Собираем объявления классов, структур и интерфейсов одним проходом и только
        /// по неймспейсам и типам: тела методов не материализуются, три полных обхода
        /// CSharpSyntaxRewriter ушли. Деревья обрабатываются параллельно, результат
        /// склеивается в порядке файлов, чтобы порядок типов был воспроизводим.
        /// </summary>
        private static void CollectTypeDeclarations(SyntaxTree[] trees)
        {
            var perTree = new List<BaseTypeDeclarationSyntax>[trees.Length];

            Parallel.For(0, trees.Length, i =>
            {
                var found = new List<BaseTypeDeclarationSyntax>(8);
                var root = trees[i].GetRoot();

                foreach (var node in root.DescendantNodes(n => n is CompilationUnitSyntax || n is BaseNamespaceDeclarationSyntax || n is TypeDeclarationSyntax))
                {
                    if (node is BaseTypeDeclarationSyntax type)
                        found.Add(type);
                }

                perTree[i] = found;
            });

            classes = new List<ClassDeclarationSyntax>(2048);
            structs = new List<StructDeclarationSyntax>(2048);
            interfaces = new List<InterfaceDeclarationSyntax>(2048);

            foreach (var found in perTree)
            {
                foreach (var type in found)
                {
                    RegisterNamespace(type);

                    switch (type)
                    {
                        case ClassDeclarationSyntax c:
                            classes.Add(c);
                            RegisterClassByName(c);
                            break;
                        case StructDeclarationSyntax s:
                            structs.Add(s);
                            break;
                        case InterfaceDeclarationSyntax i:
                            interfaces.Add(i);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// По этому словарю LinkedNodeHelper ищет родителя, а ключ у него - голое имя типа.
        /// Два разных типа с одним именем словарь не разводит: второй молча теряется,
        /// и наследник уезжает к чужому родителю вместе с его полями. Partial-части
        /// одного типа приходят сюда несколько раз, это норма и о ней не сообщаем.
        /// </summary>
        private static void RegisterClassByName(ClassDeclarationSyntax c)
        {
            var name = c.Identifier.ValueText;

            if (!classesByName.TryGetValue(name, out var alrdyHave))
            {
                classesByName.Add(name, c);
                return;
            }

            var alrdyNamespace = GetNamespaceName(alrdyHave);
            var currentNamespace = GetNamespaceName(c);

            if (alrdyNamespace == currentNamespace)
                return;

            Console.WriteLine($"конфликт имён: {name} объявлен и в {NamespaceForLog(alrdyNamespace)}, и в {NamespaceForLog(currentNamespace)} - родителя наследники увидят только из первого");
        }

        private static string NamespaceForLog(string namespaceName)
            => namespaceName ?? "глобальном неймспейсе";

        /// <summary>
        /// Неймспейс типа для таблицы using. Вложенный тип по голому имени не адресуется,
        /// using ему не поможет, поэтому вложенные и типы без неймспейса не регистрируются.
        /// </summary>
        private static void RegisterNamespace(BaseTypeDeclarationSyntax type)
        {
            if (type.Parent is BaseTypeDeclarationSyntax)
                return;

            var namespaceName = GetNamespaceName(type);

            if (namespaceName == null)
                return;

            var typeName = type.Identifier.ValueText;

            if (!namespacesByTypeName.TryGetValue(typeName, out var list))
            {
                list = new List<string>(1);
                namespacesByTypeName.Add(typeName, list);
            }

            if (!list.Contains(namespaceName))
                list.Add(namespaceName);
        }

        /// <summary>
        /// Полное имя неймспейса: обычный, file-scoped (namespace X;) и вложенные
        /// namespace A { namespace B { } } собираются подъёмом по родителям.
        /// </summary>
        private static string GetNamespaceName(SyntaxNode node)
        {
            string result = null;

            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is BaseNamespaceDeclarationSyntax ns)
                    result = result == null ? ns.Name.ToString() : ns.Name.ToString() + "." + result;
            }

            return result;
        }

        public static IReadOnlyList<string> GetNamespacesOfType(string typeName)
            => namespacesByTypeName.TryGetValue(typeName, out var list) ? list : Array.Empty<string>();

        /// <summary>
        /// Один проход по всем объявлениям вместо линейных сканов Program.classes на каждую ноду.
        /// Порядок списков внутри словарей совпадает с порядком обхода визиторами.
        /// </summary>
        private static void BuildTypeIndexes()
        {
            foreach (var c in classes)
            {
                AddToIndex(classDeclarationsByName, c.Identifier.ValueText, c);

                if (c.BaseList == null)
                    continue;

                foreach (var t in c.BaseList.Types)
                {
                    AddToIndex(childrenByBase, t.ToString(), c);

                    if (t.Type is GenericNameSyntax generic)
                        AddToIndex(childrenByGenericBase, generic.Identifier.ValueText, c);
                }
            }

            foreach (var s in structs)
                AddToIndex(structDeclarationsByName, s.Identifier.ValueText, s);

            foreach (var i in interfaces)
                AddToIndex(interfaceDeclarationsByName, i.Identifier.ValueText, i);
        }

        private static void AddToIndex<T>(Dictionary<string, List<T>> index, string key, T value)
        {
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<T>(4);
                index.Add(key, list);
            }

            list.Add(value);
        }

        public static IReadOnlyList<ClassDeclarationSyntax> GetClassDeclarations(string name)
            => classDeclarationsByName.TryGetValue(name, out var list) ? list : Array.Empty<ClassDeclarationSyntax>();

        public static IReadOnlyList<StructDeclarationSyntax> GetStructDeclarations(string name)
            => structDeclarationsByName.TryGetValue(name, out var list) ? list : Array.Empty<StructDeclarationSyntax>();

        public static IReadOnlyList<InterfaceDeclarationSyntax> GetInterfaceDeclarations(string name)
            => interfaceDeclarationsByName.TryGetValue(name, out var list) ? list : Array.Empty<InterfaceDeclarationSyntax>();

        //наследники по базовому типу: для generic-родителя ищем по идентификатору, иначе по строке
        private static IReadOnlyList<ClassDeclarationSyntax> ChildrenOf(LinkedNode node)
        {
            var index = node.IsGeneric ? childrenByGenericBase : childrenByBase;
            return index.TryGetValue(node.Name, out var list) ? list : Array.Empty<ClassDeclarationSyntax>();
        }

        private static IEnumerable<ClassDeclarationSyntax> ChildrenOfBases(params string[] baseNames)
        {
            foreach (var baseName in baseNames)
            {
                if (childrenByBase.TryGetValue(baseName, out var list))
                {
                    foreach (var c in list)
                        yield return c;
                }
            }
        }

        private static async Task<SyntaxTree> MakeTree(FileInfo f)
        {
            var s = await File.ReadAllTextAsync(f.FullName);
            var syntaxTree = CSharpSyntaxTree.ParseText(s, parseOptions);

            if (f.Name == CommandsMap)
                alrdyHaveCommandMap = f;

            return syntaxTree;
        }

        private static void CheckArgs(string[] args)
        {
            if (args == null || args.Length == 0)
                return;

            var path = args.SingleOrDefault(a => a.Contains("path:"))?.Replace("path:", "").TrimStart('-');
            var server = args.Any(a => a.Contains("server"));
            if (path != null)
            {
                ScriptsPath = path;
                ScriptsPath = Path.GetFullPath(ScriptsPath);
                if (!ScriptsPath.EndsWith(Path.DirectorySeparatorChar.ToString())) ScriptsPath += Path.DirectorySeparatorChar;

                HECSGenerated = server ? Path.Combine(ScriptsPath, "HECSGenerated") : Path.Combine(ScriptsPath, "Scripts", "HECSGenerated");
                HECSGenerated = Path.GetFullPath(HECSGenerated);
                if (!HECSGenerated.EndsWith(Path.DirectorySeparatorChar.ToString())) HECSGenerated += Path.DirectorySeparatorChar;
            }

            var defines = args.FirstOrDefault(a => a.StartsWith("defines:"))?.Replace("defines:", "");

            if (!string.IsNullOrEmpty(defines))
            {
                var symbols = defines.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(symbols);
                Console.WriteLine($"Дефайны парсера: {string.Join(", ", symbols)}");
            }

            bluePrintsNeeded = !args.Any(a => a.Contains("no_blueprints"));
            resolversNeeded = !args.Any(a => a.Contains("no_resolvers"));
            commandMapneeded = !args.Any(a => a.Contains("no_commands"));
            forceRebuild = args.Any(a => a.Contains("force_rebuild"));
        }

        private static void SaveFiles()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var processGeneration = new CodeGenerator();

            var containersPath = HECSGenerated + @"Containers" + Path.DirectorySeparatorChar;
            var resolversPath = HECSGenerated + @"Resolvers" + Path.DirectorySeparatorChar;
            var fastProvidersPath = HECSGenerated + @"FastComponentsProviders" + Path.DirectorySeparatorChar;

            //force_rebuild — единственный механизм удаления осиротевших файлов:
            //чистим директории генерата и пишем всё заново без сверки
            if (forceRebuild)
            {
                Console.WriteLine("force_rebuild: очищаем директории генерата");
                CleanDirectory(containersPath);
                CleanDirectory(resolversPath);
                CleanDirectory(fastProvidersPath);
            }

            //старые монолиты: в контейнерном режиме не генерируются и обязаны исчезнуть,
            //иначе дублируют конструктор TypesProvider / словари биндингов
            DeleteLegacyFile(HECSGenerated + TypeProvider);
            DeleteLegacyFile(HECSGenerated + SystemBindings);
            DeleteLegacyFile(HECSGenerated + "ComponentsWorldPart.cs");
            DeleteLegacyFile(HECSGenerated + "FastWorldPart.cs");
            DeleteLegacyFile(HECSGenerated + MapResolver);
            DeleteLegacyFile(HECSGenerated + CustomAndUniversalResolvers);

            SaveToFile(HecsMasks, processGeneration.GenerateHecsMasksRoslyn(), HECSGenerated);
            SaveToFile("WorldRegistration.cs", processGeneration.GetWorldRegistrationRuntime(resolversNeeded), HECSGenerated);

            //контейнеры: файл на тип, меняется только вместе со своим типом
            foreach (var component in componentOverData.Values)
            {
                if (component.IsAbstract)
                    continue;

                SaveToFile($"{component.Name}.Container.cs", processGeneration.GetComponentContainer(component, resolversNeeded), containersPath);
            }

            foreach (var system in systemOverData.Values)
            {
                if (system.IsAbstract)
                    continue;

                SaveToFile($"{system.Name}.Container.cs", processGeneration.GetSystemContainerFile(system), containersPath);
            }

            if (resolversNeeded)
            {
                var resolvers = processGeneration.GetSerializationResolvers();
                var fastComponentProviders = processGeneration.GetProvidersForFastComponent();

                SaveToFile("ResolversMapRuntime.cs", processGeneration.GetResolversMapRuntime(), HECSGenerated);

                foreach (var c in resolvers)
                    SaveToFile(c.name, c.content, resolversPath);

                foreach (var c in fastComponentProviders)
                    SaveToFile(c.fileName, c.data, fastProvidersPath);

                foreach (var fastComponent in fastComponents)
                    SaveToFile($"{fastComponent.Identifier.ValueText}.FastContainer.cs", processGeneration.GetFastComponentContainer(fastComponent), containersPath);

                foreach (var customResolver in customHecsResolvers)
                    SaveToFile($"{customResolver.Key}.CustomResolver.cs", processGeneration.GetCustomResolverRegistration(customResolver.Key, customResolver.Value), containersPath);

                foreach (var universalResolver in hecsResolverCollection)
                    SaveToFile($"{universalResolver.Value.Name}Resolver.cs", processGeneration.GetUniversalResolverFile(universalResolver.Value), resolversPath);
            }

            if (commandMapneeded)
            {
                var commandMap = processGeneration.GenerateNetworkCommandsAndShortIdsMap(networkCommands);

                if (alrdyHaveCommandMap != null)
                {
                    SaveToFileToFullPath(commandMap, alrdyHaveCommandMap.FullName);
                }
                else
                {
                    SaveToFile(CommandsMap, commandMap, HECSGenerated);
                }
            }

            if (bluePrintsNeeded)
            {
                var componetsBPFiles = processGeneration.GenerateComponentsBluePrints();
                var systemsBPFiles = processGeneration.GenerateSystemsBluePrints();
                var predicatesBPs = processGeneration.GetPredicateBluePrints();
                var actionsBPs = processGeneration.GetActionsBluePrints();
                var actionsAsyncBPs = processGeneration.GetAsyncActionsBluePrints();

                //CleanDirectory(ScriptsPath + ComponentsBluePrintsPath);
                //CleanDirectory(ScriptsPath + SystemsBluePrintsPath);

                foreach (var c in componetsBPFiles)
                    SaveToFile(c.name, c.classBody, ScriptsPath + ComponentsBluePrintsPath);

                foreach (var c in systemsBPFiles)
                    SaveToFile(c.name, c.classBody, ScriptsPath + SystemsBluePrintsPath);

                foreach (var c in predicatesBPs)
                    SaveToFile(c.Item1, c.Item2, ScriptsPath + PredicatesBlueprints);

                foreach (var c in actionsBPs)
                    SaveToFile(c.Item1, c.Item2, ScriptsPath + ActionsBlueprints);

                foreach (var c in actionsAsyncBPs)
                    SaveToFile(c.Item1, c.Item2, ScriptsPath + ActionsBlueprints);

                SaveToFile(BluePrintsProvider, processGeneration.GetBluePrintsProvider(), HECSGenerated);
            }

            var generationMs = timer.ElapsedMilliseconds;
            FlushFiles();
            timer.Stop();

            Console.WriteLine($"генерация: {generationMs}ms | запись: {timer.ElapsedMilliseconds - generationMs}ms | записано файлов: {savedFilesCount} | без изменений (пропущено): {skippedFilesCount} | ошибок записи: {failedFilesCount}");
        }

        /// <summary>
        /// Пишем накопленные файлы параллельно: директории создаём заранее в одном потоке,
        /// сверка с диском и запись идут по потокам, счётчики через Interlocked.
        /// </summary>
        private static void FlushFiles()
        {
            foreach (var directory in pendingFiles.Select(p => Path.GetDirectoryName(p.path)).Distinct())
            {
                try
                {
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                }
                catch
                {
                    Console.WriteLine("we cant create directory " + directory);
                }
            }

            Parallel.ForEach(pendingFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    if (!forceRebuild && IsSameOnDisk(file.path, file.data))
                    {
                        Interlocked.Increment(ref skippedFilesCount);
                        return;
                    }

                    File.WriteAllText(file.path, file.data);
                    Interlocked.Increment(ref savedFilesCount);
                }
                catch
                {
                    Interlocked.Increment(ref failedFilesCount);
                    Console.WriteLine("we cant save file to " + file.path);
                }
            });

            pendingFiles.Clear();
        }

        /// <summary>
        /// Сравнение с диском без учёта переводов строк: генератор пишет "\r",
        /// а IDE или Unity могут нормализовать файл, это не повод его перезаписывать.
        /// Дешёвый отсев по длине в байтах делаем до чтения.
        /// </summary>
        private static bool IsSameOnDisk(string path, string data)
        {
            var info = new FileInfo(path);

            if (!info.Exists)
                return false;

            if (info.Length == Encoding.UTF8.GetByteCount(data))
                return File.ReadAllText(path) == data;

            return NormalizeLineEndings(File.ReadAllText(path)) == NormalizeLineEndings(data);
        }

        private static string NormalizeLineEndings(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n');

        private static void DeleteLegacyFile(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    Console.WriteLine($"удалён легаси-файл генерата: {fullPath}");
                }
            }
            catch
            {
                Console.WriteLine($"не смогли удалить легаси-файл: {fullPath}");
            }
        }

        private static void CleanDirectory(string path)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
                return;

            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in directoryInfo.GetDirectories())
            {
                dir.Delete(true);
            }
        }

        //читаем всё — записываем только изменённое: диск (и компилятор за ним)
        //видит лишь реальные изменения; force_rebuild пишет без сверки (см. FlushFiles)
        private static void SaveToFile(string name, string data, string pathToDirectory)
        {
            pendingFiles.Add((pathToDirectory + name, data));
        }

        private static void SaveToFileToFullPath(string data, string fullPath)
        {
            pendingFiles.Add((fullPath, data));
        }


        private static void ProcessStructs(StructDeclarationSyntax s)
        {
            var structCurrent = s.Identifier.ValueText;
            structByName.TryAdd(structCurrent, s);

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains(typeof(IGlobalCommand).Name)))
            {
                globalCommands.AddOrRemoveElement(s, true);
                localCommands.AddOrRemoveElement(s, true);
                Console.WriteLine("нашли глобальную команду " + structCurrent);
            }

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains("IFastComponent")))
            {
                fastComponents.AddOrRemoveElement(s, true);
            }

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains(typeof(ICommand).Name)))
            {
                localCommands.AddOrRemoveElement(s, true);
                Console.WriteLine("нашли локальную команду " + structCurrent);
            }

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains("INetworkCommand") || x.ToString().Contains("INetworkLocalCommand")))
            {
                globalCommands.AddOrRemoveElement(s, true);
                localCommands.AddOrRemoveElement(s, true);
                networkCommands.AddOrRemoveElement(s, true);
                Console.WriteLine("нашли локальную команду " + structCurrent);
            }

            //we add here custom resolvers what alrdy on project
            if (s.AttributeLists.Count > 0)
            {
                foreach (var a in s.AttributeLists)
                {
                    foreach (var attr in a.Attributes)
                    {
                        if (attr.IsAttribute(HECSManualResolver))
                        {
                            var arguments = attr.ArgumentList.Arguments;

                            foreach (var arg in arguments)
                            {
                                if (arg.Expression is TypeOfExpressionSyntax needed)
                                {
                                    if (needed.Type is IdentifierNameSyntax identifierNameSyntax)
                                    {
                                        var needeType = identifierNameSyntax.Identifier.ValueText;
                                        customHecsResolvers.Add(needeType, new ResolverData { TypeToResolve = needeType, ResolverName = s.Identifier.ValueText });
                                    }
                                    else if (needed.Type is PredefinedTypeSyntax predefinedTypeSyntax)
                                    {
                                        var needeType = predefinedTypeSyntax.ToString();
                                        customHecsResolvers.Add(needeType, new ResolverData { TypeToResolve = needeType, ResolverName = s.Identifier.ValueText });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public static IEnumerable<INamedTypeSymbol> GetTypesByMetadataName(Compilation compilation, string typeMetadataName)
        {
            return compilation.References
                .Select(compilation.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>()
                .Select(assemblySymbol => assemblySymbol.GetTypeByMetadataName(typeMetadataName))
                .Where(t => t != null);
        }

        private static void ProcessInterfaces()
        {
            foreach (var i in interfacesOverData)
            {
                if (i.Value.isPartial)
                {
                    foreach (var p in i.Value.Parts)
                    {
                        ProcessInterfaceBaseList(p, i);
                    }
                }
                else
                {
                    ProcessInterfaceBaseList(i.Value.InterfaceDeclaration, i);
                }
            }

            foreach (var c in classes)
            {
                var baseTypes = c.BaseList;

                if (baseTypes != null)
                {
                    foreach (var t in baseTypes.DescendantNodes())
                    {
                        if (interfacesOverData.TryGetValue(t.ToString(), out var interfaceLinked))
                        {
                            interfaceCache.Clear();
                            interfaceLinked.GetInterfaces(interfaceCache);

                            foreach (var i in interfaceCache)
                            {
                                var nodes = i.InterfaceDeclaration.BaseList?.DescendantNodes();

                                if (nodes != null)
                                {
                                    foreach (var baseNode in nodes)
                                    {
                                        ProcessGenericInterface(baseNode);
                                    }
                                }
                            }
                        }

                        ProcessGenericInterface(t);
                    }
                }
            }
        }

        private static void ProcessGenericInterface(SyntaxNode t)
        {
            if (t is GenericNameSyntax generic)
            {
                if (genericInterfacesOverData.ContainsKey(t.ToString())) return;

                if (interfacesOverData.TryGetValue(generic.Identifier.ToString(), out var linkedInterface))
                {
                    var tp = generic;
                    genericInterfacesOverData.Add(t.ToString(), new LinkedGenericInterfaceNode
                    {
                        BaseInterface = linkedInterface,
                        GenericNameSyntax = generic,
                        GenericType = generic.TypeArgumentList.Arguments[0].ToString(),
                        MultiArguments = generic.TypeArgumentList.Arguments.Count > 1,
                        Name = t.ToString(),
                    });
                }
            }
        }

        private static void ProcessInterfaceBaseList(InterfaceDeclarationSyntax p, KeyValuePair<string, LinkedInterfaceNode> i)
        {
            var partBaseList = p.BaseList;

            if (partBaseList == null) return;

            foreach (var baseType in partBaseList.Types)
            {
                var key = baseType.ToString();

                if (interfacesOverData.ContainsKey(key))
                {
                    i.Value.Parents.Add(interfacesOverData[key]);

                    if (!i.Value.isHaveReact && interfacesOverData[key].isHaveReact)
                        i.Value.isHaveReact = true;
                }
            }
        }

        private static void ProcessClasses()
        {
            foreach (var comp in componentOverData.Values)
            {
                if (comp.IsAbstract) continue;
                componentsDeclarations.Add(comp.ClassDeclaration);
            }

            //we gather here classes 
            foreach (var c in classes)
            {
                if (c.AttributeLists.Count > 0)
                {
                    foreach (var a in c.AttributeLists)
                    {
                        foreach (var attr in a.Attributes)
                        {
                            if (attr.IsAttribute(HECSResolver))
                            {
                                var name = c.Identifier.ValueText;
                                hecsResolverCollection.Add(c.Identifier.ValueText, LinkedNodeHelper.GetLinkedNode(c));
                                customHecsResolvers.Add(name, new ResolverData { TypeToResolve = name, ResolverName = name + Resolver });
                            }
                        }
                    }
                }
            }
        }

        private static void GatherComponents()
        {
            var pureComponents = ChildrenOfBases("BaseComponent", "IComponent").Where(x => x.Identifier.ValueText != "BaseComponent");

            foreach (var component in pureComponents)
            {
                var name = component.Identifier.ValueText;

                if (componentOverData.ContainsKey(name))
                {
                    continue;
                }

                componentOverData.Add(component.Identifier.ValueText, new LinkedNode
                {
                    Name = name,
                    ClassDeclaration = component,
                    Parent = null,
                    IsAbstract = component.Modifiers.Any(x => x.ValueText == "abstract"),
                    IsPartial = component.Modifiers.Any(x => x.ValueText == "partial"),
                    IsGeneric = component.TypeParameterList != null,
                    Parts = new HashSet<ClassDeclarationSyntax>(),
                    Interfaces = new HashSet<LinkedInterfaceNode>(),
                });

                if (componentOverData[name].IsPartial)
                {
                    componentOverData[name].Parts.Add(componentOverData[name].ClassDeclaration);
                    var parts = GetClassDeclarations(name);

                    foreach (var part in parts)
                    {
                        componentOverData[name].Parts.Add(part);

                        var baseList = part.BaseList;

                        if (baseList != null)
                        {
                            foreach (var tp in baseList.Types)
                            {
                                if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                                {
                                    componentOverData[name].Interfaces.Add(node);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var baseList = component.BaseList?.Types;
                    componentOverData[name].Parts.Add(componentOverData[name].ClassDeclaration);

                    if (baseList != null)
                    {
                        foreach (var tp in baseList)
                        {
                            if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                            {
                                componentOverData[name].Interfaces.Add(node);
                            }
                        }
                    }
                }
            }

            foreach (var ln in componentOverData.Values.ToArray())
            {
                ProcessLinkNodesComponents(ln);
            }
        }

        private static void GatherSystems()
        {
            var pureSystems = ChildrenOfBases("BaseSystem", "ISystem").Where(x => x.Identifier.ValueText != "BaseSystem");

            foreach (var sys in pureSystems)
            {
                var name = sys.Identifier.ValueText;

                if (systemOverData.ContainsKey(name))
                {
                    continue;
                }

                systemOverData.Add(sys.Identifier.ValueText, new LinkedNode
                {
                    Name = name,
                    ClassDeclaration = sys,
                    Parent = null,
                    IsAbstract = sys.Modifiers.Any(x => x.ValueText == "abstract"),
                    IsPartial = sys.Modifiers.Any(x => x.ValueText == "partial"),
                    Parts = new HashSet<ClassDeclarationSyntax>(),
                    IsGeneric = sys.TypeParameterList != null,
                    Interfaces = new HashSet<LinkedInterfaceNode>(),
                });

                if (systemOverData[name].IsPartial)
                {
                    var parts = GetClassDeclarations(name);

                    foreach (var part in parts)
                    {
                        systemOverData[name].Parts.Add(part);

                        var baseList = part.BaseList;

                        if (baseList != null)
                        {
                            foreach (var tp in baseList.Types)
                            {
                                if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                                {
                                    systemOverData[name].Interfaces.Add(node);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var baseList = sys.BaseList?.Types;

                    if (baseList != null)
                    {
                        foreach (var tp in baseList)
                        {
                            if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                            {
                                systemOverData[name].Interfaces.Add(node);
                            }
                        }
                    }
                }
            }

            foreach (var ln in systemOverData.Values.ToArray())
            {
                ProcessLinkNodes(ln);
            }
        }

        private static void ProcessLinkNodesComponents(LinkedNode linkedNode)
        {
            var children = ChildrenOf(linkedNode);

            foreach (var component in children)
            {
                var name = component.Identifier.ValueText;
                if (componentOverData.ContainsKey(name))
                {
                    continue;
                }

                componentOverData.Add(component.Identifier.ValueText, new LinkedNode
                {
                    Name = name,
                    ClassDeclaration = component,
                    Parent = null,
                    IsAbstract = component.Modifiers.Any(x => x.ValueText == "abstract"),
                    IsPartial = component.Modifiers.Any(x => x.ValueText == "partial"),
                    IsGeneric = component.TypeParameterList != null,
                    Parts = new HashSet<ClassDeclarationSyntax>(8),
                    Interfaces = new HashSet<LinkedInterfaceNode>(8),
                });

                if (componentOverData[name].IsPartial)
                {
                    componentOverData[name].Parts.Add(componentOverData[name].ClassDeclaration);
                    var parts = GetClassDeclarations(name);

                    foreach (var part in parts)
                    {
                        if (part == component) continue;
                        componentOverData[name].Parts.Add(part);

                        if (interfacesOverData.TryGetValue(part.ToString(), out var node))
                        {
                            componentOverData[name].Interfaces.Add(node);
                        }
                    }
                }
                else
                {
                    componentOverData[name].Parts.Add(componentOverData[name].ClassDeclaration);
                    var baseList = component.BaseList?.Types;

                    if (baseList != null)
                    {
                        foreach (var tp in baseList)
                        {
                            if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                            {
                                componentOverData[name].Interfaces.Add(node);
                            }
                        }
                    }
                }

                componentOverData[name].Parent = linkedNode;
                ProcessLinkNodesComponents(componentOverData[name]);
            }
        }

        private static void ProcessLinkNodes(LinkedNode linkedNode)
        {
            var children = ChildrenOf(linkedNode);

            foreach (var sys in children)
            {
                var name = sys.Identifier.ValueText;
                if (systemOverData.ContainsKey(name))
                {
                    continue;
                }

                systemOverData.Add(sys.Identifier.ValueText, new LinkedNode
                {
                    Name = name,
                    ClassDeclaration = sys,
                    Parent = null,
                    IsAbstract = sys.Modifiers.Any(x => x.ValueText == "abstract"),
                    IsPartial = sys.Modifiers.Any(x => x.ValueText == "partial"),
                    Parts = new HashSet<ClassDeclarationSyntax>(8),
                    IsGeneric = sys.TypeParameterList != null,
                    Interfaces = new HashSet<LinkedInterfaceNode>(8),
                });

                if (systemOverData[name].IsPartial)
                {
                    var parts = GetClassDeclarations(name);

                    foreach (var part in parts)
                    {
                        if (part == sys) continue;
                        systemOverData[name].Parts.Add(part);

                        if (interfacesOverData.TryGetValue(part.ToString(), out var node))
                        {
                            systemOverData[name].Interfaces.Add(node);
                        }
                    }
                }
                else
                {
                    var baseList = sys.BaseList?.Types;

                    if (baseList != null)
                    {
                        foreach (var tp in baseList)
                        {
                            if (interfacesOverData.TryGetValue(tp.ToString(), out var node))
                            {
                                systemOverData[name].Interfaces.Add(node);
                            }
                        }
                    }
                }

                systemOverData[name].Parent = linkedNode;
                ProcessLinkNodes(systemOverData[name]);
            }
        }
    }

    public class LinkedNode
    {
        public string Name;
        public ClassDeclarationSyntax ClassDeclaration;
        public LinkedNode Parent;
        public bool IsAbstract;
        public bool IsPartial;
        public bool IsGeneric;

        public IEnumerable<LinkedNode> GetParents()
        {
            var currentNode = this;

            while (currentNode.Parent != null)
            {
                currentNode = currentNode.Parent;
                yield return currentNode;
            }

            yield break;
        }

        //containts parts includes itself
        public HashSet<ClassDeclarationSyntax> Parts = new HashSet<ClassDeclarationSyntax>(8);

        //include all interfaces, include from parents
        public HashSet<LinkedInterfaceNode> Interfaces = new HashSet<LinkedInterfaceNode>(8);

        public override bool Equals(object obj)
        {
            return obj is LinkedNode node &&
                   Name == node.Name &&
                   EqualityComparer<ClassDeclarationSyntax>.Default.Equals(ClassDeclaration, node.ClassDeclaration);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, ClassDeclaration);
        }

        public void GetInterfaces(HashSet<LinkedInterfaceNode> interfaces)
        {
            if (IsPartial)
            {
                foreach (var p in Parts)
                {
                    if (p.BaseList != null)
                    {
                        foreach (var t in p.BaseList.Types)
                        {
                            if (Program.interfacesOverData.TryGetValue(t.ToString(), out var node))
                            {
                                interfaces.Add(node);
                            }
                        }
                    }
                }
            }
            else
            {
                if (ClassDeclaration.BaseList != null)
                {
                    foreach (var t in ClassDeclaration.BaseList.Types)
                    {
                        if (Program.interfacesOverData.TryGetValue(t.ToString(), out var node))
                        {
                            interfaces.Add(node);
                        }
                    }
                }
            }

            foreach (var i in interfaces.ToArray())
            {
                i.GetInterfaces(interfaces);
            }

            if (Parent != null)
                Parent.GetInterfaces(interfaces);
        }

        public void GetGenericInterfaces(HashSet<LinkedGenericInterfaceNode> interfaces)
        {
            Parent?.GetGenericInterfaces(interfaces);

            if (IsPartial)
            {
                foreach (var p in Parts)
                {
                    var baseList = p.BaseList?.Types;

                    if (baseList == null) continue;

                    foreach (var type in baseList)
                    {
                        if (Program.genericInterfacesOverData.TryGetValue(type.ToString(), out var node))
                        {
                            interfaces.Add(node);
                        }

                        foreach (var i in Interfaces)
                        {
                            i.GetGenericInterfaces(interfaces);
                        }
                    }
                }
            }
            else
            {
                var baseList = ClassDeclaration.BaseList?.Types;

                if (baseList == null) return;

                foreach (var type in baseList)
                {
                    if (Program.genericInterfacesOverData.TryGetValue(type.ToString(), out var node))
                    {
                        interfaces.Add(node);
                    }

                    foreach (var i in Interfaces)
                    {
                        i.GetGenericInterfaces(interfaces);
                    }
                }
            }
        }

        public void GetAllParentsAndParts(HashSet<ClassDeclarationSyntax> classDeclarationSyntaxes)
        {
            classDeclarationSyntaxes.Add(ClassDeclaration);

            if (IsPartial)
            {
                foreach (var p in Parts)
                    classDeclarationSyntaxes.Add(p);
            }
            else
                classDeclarationSyntaxes.Add(ClassDeclaration);

            if (Parent != null)
                Parent.GetAllParentsAndParts(classDeclarationSyntaxes);
        }
    }

    public class LinkedGenericInterfaceNode
    {
        public string Name;
        public GenericNameSyntax GenericNameSyntax;
        public string GenericType;
        public LinkedInterfaceNode BaseInterface;
        public bool MultiArguments;
    }

    public class LinkedInterfaceNode
    {
        public string Name;
        public InterfaceDeclarationSyntax InterfaceDeclaration;
        public HashSet<InterfaceDeclarationSyntax> Parts;
        public HashSet<LinkedInterfaceNode> Parents;
        public bool isHaveReact;
        public bool isPartial;

        public override bool Equals(object obj)
        {
            return obj is LinkedInterfaceNode node &&
                   Name == node.Name &&
                   EqualityComparer<InterfaceDeclarationSyntax>.Default.Equals(InterfaceDeclaration, node.InterfaceDeclaration);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, InterfaceDeclaration);
        }

        public void GetInterfaces(HashSet<LinkedInterfaceNode> interfaces)
        {
            if (interfaces.Contains(this))
                return;

            interfaces.Add(this);

            foreach (var p in Parents)
            {
                p.GetInterfaces(interfaces);
            }

            if (isPartial)
            {
                foreach (var p in Parts)
                {
                    var baseList = p.BaseList?.Types;

                    if (baseList == null) continue;

                    foreach (var type in baseList)
                    {
                        if (Program.interfacesOverData.TryGetValue(type.ToString(), out var node))
                        {
                            node.GetInterfaces(interfaces);
                        }
                    }
                }
            }
        }

        public void GetGenericInterfaces(HashSet<LinkedGenericInterfaceNode> interfaces)
        {
            foreach (var p in Parents)
            {
                p.GetGenericInterfaces(interfaces);
            }

            if (isPartial)
            {
                foreach (var p in Parts)
                {
                    var baseList = p.BaseList?.Types;

                    if (baseList == null) continue;

                    foreach (var type in baseList)
                    {
                        if (Program.genericInterfacesOverData.TryGetValue(type.ToString(), out var node))
                        {
                            interfaces.Add(node);
                        }
                    }
                }
            }
            else
            {
                var baseList = InterfaceDeclaration.BaseList?.Types;

                if (baseList == null) return;

                foreach (var type in baseList)
                {
                    if (Program.genericInterfacesOverData.TryGetValue(type.ToString(), out var node))
                    {
                        interfaces.Add(node);
                    }
                }
            }
        }
    }
}