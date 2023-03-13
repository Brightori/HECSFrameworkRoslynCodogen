using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HECSFramework.Core;
using HECSFramework.Core.Generator;
using HECSFramework.Core.Helpers;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
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

        public static string ScriptsPath = @"D:\Develop\Shootervertical\Assets\";
        public static string HECSGenerated = @"D:\Develop\Shootervertical\Assets\Scripts\HECSGenerated\";
        //public static string ScriptsPath = @"E:\repos\Kefir\minilife-server\MinilifeServer\";
        //public static string HECSGenerated = @"E:\repos\Kefir\minilife-server\MinilifeServer\HECSGenerated\";

        private const string TypeProvider = "TypeProvider.cs";
        private const string MaskProvider = "MaskProvider.cs";
        private const string HecsMasks = "HECSMasks.cs";
        private const string SystemBindings = "SystemBindings.cs";
        private const string ComponentContext = "ComponentContext.cs";
        private const string BluePrintsProvider = "BluePrintsProvider.cs";
        private const string Documentation = "Documentation.cs";
        private const string MapResolver = "MapResolver.cs";
        private const string CustomAndUniversalResolvers = "CustomAndUniversalResolvers.cs";
        private const string CommandsMap = "CommandsMap.cs";

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

        public static bool CommandMapNeeded => commandMapneeded;

        private static HashSet<LinkedInterfaceNode> interfaceCache = new HashSet<LinkedInterfaceNode>(32);
        private static List<FileInfo> files;

        private static FileInfo alrdyHaveCommandMap;
        public static CSharpCompilation Compilation;

        static async Task Main(string[] args)
        {
            CheckArgs(args);

            Console.WriteLine($"Путь: {ScriptsPath}");
            Console.WriteLine($"Путь кодогена: {HECSGenerated}");
            Console.WriteLine($"Найдены аргументы запуска: {string.Join(", ", args)}");
            Console.WriteLine($"Доступные аргументы: {Environment.NewLine}{string.Join(Environment.NewLine, new[] { "path:путь_до_скриптов", "no_blueprints", "no_resolvers", "no_commands", "server" })}");

            var test = Directory.GetDirectories(ScriptsPath);

            //var files = new DirectoryInfo(ScriptsPath).GetFiles("*.cs", SearchOption.AllDirectories);
            files = new DirectoryInfo(ScriptsPath).GetFiles("*.cs", SearchOption.AllDirectories).Where(x => !x.FullName.Contains("\\Plugins") && !x.FullName.Contains("\\HECSGenerated") && !x.FullName.Contains("\\MessagePack")).ToList();
            Console.WriteLine(files.Count);

            var list = new List<SyntaxTree>(2048);

            foreach (var f in files)
            {
                if (f.Extension == ".cs")
                {
                    var s = File.ReadAllText(f.FullName);
                    var syntaxTree = CSharpSyntaxTree.ParseText(s);
                    list.Add(syntaxTree);

                    if (f.Name == CommandsMap)
                        alrdyHaveCommandMap = f;
                }
            }

            Compilation = CSharpCompilation.Create("HelloWorld").AddSyntaxTrees(list);


            var classVisitor = new ClassVirtualizationVisitor();
            var structVisitor = new StructVirtualizationVisitor();
            var interfaceVisitor = new InterfaceVirtualizationVisitor();

            foreach (var syntaxTree in list)
            {
                classVisitor.Visit(syntaxTree.GetRoot());
                structVisitor.Visit(syntaxTree.GetRoot());
                interfaceVisitor.Visit(syntaxTree.GetRoot());
            }

            classes = classVisitor.Classes;
            structs = structVisitor.Structs;
            interfaces = interfaceVisitor.Interfaces;

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
                    var parts = interfaces.Where(x => x.Identifier.ValueText == name).ToHashSet();
                    node.Parts = parts;
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

            Console.WriteLine("нашли компоненты " + componentOverData.Count);
            SaveFiles();
            Console.WriteLine("успешно сохранено");
            //Thread.Sleep(1500);
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

            bluePrintsNeeded = !args.Any(a => a.Contains("no_blueprints"));
            resolversNeeded = !args.Any(a => a.Contains("no_resolvers"));
            commandMapneeded = !args.Any(a => a.Contains("no_commands"));
        }

        private static void SaveFiles()
        {
            var processGeneration = new CodeGenerator();
            SaveToFile(TypeProvider, processGeneration.GenerateTypesMapRoslyn(), HECSGenerated);
            //SaveToFile(MaskProvider, processGeneration.GenerateMaskProviderRoslyn(), HECSGenerated);
            SaveToFile(SystemBindings, processGeneration.GetSystemBindsByRoslyn(), HECSGenerated);
            //SaveToFile(ComponentContext, processGeneration.GetComponentContextRoslyn(), HECSGenerated);
            SaveToFile(HecsMasks, processGeneration.GenerateHecsMasksRoslyn(), HECSGenerated);
            //SaveToFile(Documentation, processGeneration.GetDocumentationRoslyn(), HECSGenerated); не получается нормально автоматизировать, слишком сложные параметры у атрибута

            SaveToFile("ComponentsWorldPart.cs", processGeneration.GetEntitiesWorldPart(), HECSGenerated);

            if (resolversNeeded)
            {
                var path = HECSGenerated + @"Resolvers\";
                var fastProvidersPath = HECSGenerated + @"FastComponentsProviders\";
                var resolvers = processGeneration.GetSerializationResolvers();
                var fastComponents = processGeneration.GetProvidersForFastComponent();
                SaveToFile(MapResolver, processGeneration.GetResolverMap(), HECSGenerated);
                SaveToFile(CustomAndUniversalResolvers, processGeneration.GetCustomResolversMap(), HECSGenerated);
                SaveToFile("FastWorldPart.cs", processGeneration.GetFastWorldPart(), HECSGenerated);

                CleanDirectory(path);

                foreach (var c in resolvers)
                    SaveToFile(c.name, c.content, path);

                foreach (var c in fastComponents)
                    SaveToFile(c.fileName, c.data, fastProvidersPath);
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

                SaveToFile(BluePrintsProvider, processGeneration.GetBluePrintsProvider(), HECSGenerated, needToImport: true);
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

        private static void SaveToFile(string name, string data, string pathToDirectory, bool needToImport = false)
        {
            var path = pathToDirectory + name;

            try
            {
                if (!Directory.Exists(pathToDirectory))
                    Directory.CreateDirectory(pathToDirectory);

                File.WriteAllText(path, data);
            }
            catch
            {
                Console.WriteLine("we cant save file to " + pathToDirectory);
            }
        }

        private static void SaveToFileToFullPath(string data, string fullPath)
        {
            try
            {
                File.WriteAllText(fullPath, data);
            }
            catch
            {
                Console.WriteLine("we cant save file to " + fullPath);
            }
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
                        if (attr.Name.ToString().Contains(HECSManualResolver))
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
                            if (attr.ToString().Contains(HECSResolver))
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
            var pureComponents = classes.Where(x => x.Identifier.ValueText != "BaseComponent" && x.BaseList != null && x.BaseList.Types.Any(z => z.ToString() == "BaseComponent" || z.ToString() == "IComponent"));

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
                    var parts = classes.Where(x => x.Identifier.ValueText == name);

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
            var pureSystems = classes.Where(x => x.Identifier.ValueText != "BaseSystem" && x.BaseList != null && x.BaseList.Types.Any(z => z.ToString() == "BaseSystem" || z.ToString() == "ISystem"));

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
                    Interfaces = new HashSet<LinkedInterfaceNode>(),
                });

                if (systemOverData[name].IsPartial)
                {
                    var parts = classes.Where(x => x.Identifier.ValueText == name);

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
            IEnumerable<ClassDeclarationSyntax> children = null;

            if (linkedNode.IsGeneric)
            {
                children = classes.Where(x => x.BaseList != null && x.BaseList.Types.Any(z => z.Type is GenericNameSyntax nameSyntax && nameSyntax.Identifier.ValueText == linkedNode.Name));
            }
            else
                children = classes.Where(x => x.BaseList != null && x.BaseList.Types.Any(z => z.ToString() == linkedNode.Name));

            var neede = classes.FirstOrDefault(x => x.Identifier.ValueText == "BaseDefence");

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
                    var parts = classes.Where(x => x.Identifier.ValueText == name);

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
            var children = classes.Where(x => x.BaseList != null && x.BaseList.Types.Any(z => z.ToString() == linkedNode.Name));

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
                    Interfaces = new HashSet<LinkedInterfaceNode>(8),
                });

                if (systemOverData[name].IsPartial)
                {
                    var parts = classes.Where(x => x.Identifier.ValueText == name);

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

        class ClassVirtualizationVisitor : CSharpSyntaxRewriter
        {
            public ClassVirtualizationVisitor()
            {
                Classes = new List<ClassDeclarationSyntax>(2048);
            }

            public List<ClassDeclarationSyntax> Classes { get; set; }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                base.VisitClassDeclaration(node);
                Classes.Add(node); // save your visited classes

                if (!Program.classesByName.ContainsKey(node.Identifier.ValueText))
                {
                    Program.classesByName.Add(node.Identifier.ValueText, node);
                }

                return node;
            }
        }

        class StructVirtualizationVisitor : CSharpSyntaxRewriter
        {
            public StructVirtualizationVisitor()
            {
                Structs = new List<StructDeclarationSyntax>(2048);
            }

            public List<StructDeclarationSyntax> Structs { get; set; }

            public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
            {
                node = (StructDeclarationSyntax)base.VisitStructDeclaration(node);
                Structs.Add(node); // save your visited classes
                return node;
            }
        }

        class InterfaceVirtualizationVisitor : CSharpSyntaxRewriter
        {
            public List<InterfaceDeclarationSyntax> Interfaces { get; set; }

            public InterfaceVirtualizationVisitor()
            {
                Interfaces = new List<InterfaceDeclarationSyntax>(2048);
            }

            public override SyntaxNode Visit(SyntaxNode node)
            {
                if (node is InterfaceDeclarationSyntax inter)
                    VisitInterfaceDeclaration(inter);

                return base.Visit(node);
            }

            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            {
                Interfaces.Add(node);
                return base.VisitInterfaceDeclaration(node);
            }
        }

        private static VisualStudioInstance SelectVisualStudioInstance(VisualStudioInstance[] visualStudioInstances)
        {
            Console.WriteLine("Multiple installs of MSBuild detected please select one:");
            for (int i = 0; i < visualStudioInstances.Length; i++)
            {
                Console.WriteLine($"Instance {i + 1}");
                Console.WriteLine($"    Name: {visualStudioInstances[i].Name}");
                Console.WriteLine($"    Version: {visualStudioInstances[i].Version}");
                Console.WriteLine($"    MSBuild Path: {visualStudioInstances[i].MSBuildPath}");
            }

            while (true)
            {
                var userResponse = Console.ReadLine();
                if (int.TryParse(userResponse, out int instanceNumber) &&
                    instanceNumber > 0 &&
                    instanceNumber <= visualStudioInstances.Length)
                {
                    return visualStudioInstances[instanceNumber - 1];
                }
                Console.WriteLine("Input not accepted, try again.");
            }
        }

        private class ConsoleProgressReporter : IProgress<ProjectLoadProgress>
        {
            public void Report(ProjectLoadProgress loadProgress)
            {
                var projectDisplay = Path.GetFileName(loadProgress.FilePath);
                if (loadProgress.TargetFramework != null)
                {
                    projectDisplay += $" ({loadProgress.TargetFramework})";
                }

                Console.WriteLine($"{loadProgress.Operation,-15} {loadProgress.ElapsedTime,-15:m\\:ss\\.fffffff} {projectDisplay}");
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