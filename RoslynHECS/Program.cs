using HECSFramework.Core;
using HECSFramework.Core.Generator;
using HECSFramework.Core.Helpers;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClassDeclarationSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace RoslynHECS
{
    class Program
    {
        public static List<string> components = new List<string>(256);
        public static List<string> systems = new List<string>(256);
        public static List<ClassDeclarationSyntax> componentsDeclarations = new List<ClassDeclarationSyntax>(256);
        public static List<ClassDeclarationSyntax> systemsDeclarations = new List<ClassDeclarationSyntax>(256);
        public static List<StructDeclarationSyntax> globalCommands = new List<StructDeclarationSyntax>(256);
        public static List<StructDeclarationSyntax> localCommands = new List<StructDeclarationSyntax>(256);
        public static List<StructDeclarationSyntax> networkCommands = new List<StructDeclarationSyntax>(256);
        
        public static List<ClassDeclarationSyntax> classes;
        public static List<StructDeclarationSyntax> structs;
        public static List<InterfaceDeclarationSyntax> interfaces;

        public static string ScriptsPath = @"D:\Develop\CyberMafia\Assets\";
        public static string HECSGenerated = @"D:\Develop\CyberMafia\Assets\Scripts\HECSGenerated\";
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

        private const string ComponentsBluePrintsPath = "/Scripts/BluePrints/ComponentsBluePrints/";
        private const string SystemsBluePrintsPath = "/Scripts/BluePrints/SystemsBluePrint/";

        private const string BaseComponent = "BaseComponent";

        private static bool resolversNeeded = true;
        private static bool bluePrintsNeeded = true;
        private static bool commandMapneeded = false;

        static async Task Main(string[] args)
        {
            CheckArgs(args);
            Console.WriteLine($"Путь: {ScriptsPath}");
            Console.WriteLine($"Путь кодогена: {HECSGenerated}");

            var files = new DirectoryInfo(ScriptsPath).GetFiles("*.cs", SearchOption.AllDirectories);
            var list = new List<SyntaxTree>();

            foreach (var f in files)
            {
                if (f.Extension == ".cs")
                {
                    var s = File.ReadAllText(f.FullName);
                    var syntaxTree = CSharpSyntaxTree.ParseText(s);
                    list.Add(syntaxTree);
                }
            }

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

            foreach (var c in classes)
            {
                //Console.WriteLine(c.Identifier.ValueText);
                ProcessClasses(c);
            }
                

            foreach (var s in structs)
                ProcessStructs(s);

            SaveFiles();

            Console.WriteLine("нашли компоненты " + components.Count);
            Console.WriteLine($"Найдены аргументы запуска: {string.Join(", ", args)}");
            Console.WriteLine($"Доступные аргументы: {Environment.NewLine}{string.Join(Environment.NewLine, new[] { "path:путь_до_скриптов", "no_blueprints", "no_resolvers", "no_commands", "server" })}");
            Console.ReadKey();
        }

        private static void CheckArgs(string[] args)
		{
            if (args == null || args.Length == 0)
                return;

            var path = args.SingleOrDefault(a => a.Contains("path:"))?.Replace("path:","").TrimStart('-');
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
            SaveToFile(MaskProvider, processGeneration.GenerateMaskProviderRoslyn(), HECSGenerated);
            SaveToFile(SystemBindings, processGeneration.GetSystemBindsByRoslyn(), HECSGenerated);
            SaveToFile(ComponentContext, processGeneration.GetComponentContextRoslyn(), HECSGenerated);
            SaveToFile(HecsMasks, processGeneration.GenerateHecsMasksRoslyn(), HECSGenerated);
            //SaveToFile(Documentation, processGeneration.GetDocumentationRoslyn(), HECSGenerated); не получается нормально автоматизировать, слишком сложные параметры у атрибута

            if (resolversNeeded)
            {
                var path = HECSGenerated + @"Resolvers\";
                var resolvers = processGeneration.GetSerializationResolvers();
                SaveToFile(MapResolver, processGeneration.GetResolverMap(), HECSGenerated);

                CleanDirectory(path);

                foreach (var c in resolvers)
                    SaveToFile(c.name, c.content, path);
            }

            if (commandMapneeded)
            {
                var commandMap = processGeneration.GenerateNetworkCommandsMap(networkCommands);
                SaveToFile("CommandsMap.cs", commandMap, HECSGenerated);
            }

            if (bluePrintsNeeded)
            {
                var componetsBPFiles = processGeneration.GenerateComponentsBluePrints();
                var systemsBPFiles = processGeneration.GenerateSystemsBluePrints();

                //CleanDirectory(ScriptsPath + ComponentsBluePrintsPath);
                //CleanDirectory(ScriptsPath + SystemsBluePrintsPath);

                foreach (var c in componetsBPFiles)
                    SaveToFile(c.name, c.classBody, ScriptsPath + ComponentsBluePrintsPath);

                foreach (var c in systemsBPFiles)
                    SaveToFile(c.name, c.classBody, ScriptsPath + SystemsBluePrintsPath);

                SaveToFile(BluePrintsProvider, processGeneration.GetBluePrintsProvider(),HECSGenerated, needToImport: true);
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
                Console.WriteLine("не смогли ослить " + pathToDirectory);
            }
        }

        private static void ProcessStructs(StructDeclarationSyntax s)
        {
            var structCurrent = s.Identifier.ValueText;

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains(typeof(IGlobalCommand).Name)))
            {
                globalCommands.AddOrRemoveElement(s, true);
                localCommands.AddOrRemoveElement(s,true);
                Console.WriteLine("нашли глобальную команду " + structCurrent);
            }

            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains(typeof(ICommand).Name)))
            {
                localCommands.AddOrRemoveElement(s, true);
                Console.WriteLine("нашли локальную команду " + structCurrent);
            }    
            
            if (s.BaseList != null && s.BaseList.ChildNodes().Any(x => x.ToString().Contains("INetworkCommand")))
            {
                globalCommands.AddOrRemoveElement(s, true);
                localCommands.AddOrRemoveElement(s, true);
                networkCommands.AddOrRemoveElement(s, true);
                Console.WriteLine("нашли локальную команду " + structCurrent);
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

        private static void ProcessClasses(ClassDeclarationSyntax c)
        {
            var classCurrent = c.Identifier.ValueText;

            //todo это костыль для избегания рекурсий из за дженериков в которые передаётся аргументом тип реализатора
            //можно попробать добавить счётчик или запилить проверку 
            if (c.ConstraintClauses != null && c.ConstraintClauses.Count > 0)
                return;

            var baseClass = c.BaseList != null ? c.BaseList.ChildNodes()?.ToArray() : new SyntaxNode[0];
                var isAbstract = c.Modifiers.Any(x => x.IsKind(SyntaxKind.AbstractKeyword));

                if (IsComponent(c) && !isAbstract)
                {
                    if (components.Contains(classCurrent))
                        return;

                    components.Add(classCurrent);
                    componentsDeclarations.Add(c);
                    Console.WriteLine("нашли компонент " + classCurrent);
                }

                if (IsSystem(c) && !isAbstract && !classCurrent.Contains("SystemBluePrint"))
                {
                    if (systems.Contains(classCurrent))
                        return;

                    systems.Add(classCurrent);
                    systemsDeclarations.Add(c);

                    Console.WriteLine("----");
                    Console.WriteLine("нашли систему " + classCurrent);
                }
        }

        private static bool IsComponent(ClassDeclarationSyntax c)
        {
            var baseClass = c.BaseList != null ? c.BaseList.ChildNodes()?.ToList() : new List<SyntaxNode>(0);

            if (baseClass.Count == 0)
                return false;

            if (baseClass.Any(x => x.ToString().Contains(BaseComponent)))
                return true;

            var gatherParents = classes.Where(x => baseClass.Any(z => z.ToString() == x.Identifier.ValueText));

            foreach (var parent in gatherParents)
            {
                if (IsComponent(parent))
                    return true;
            }

            return false;
        }

        private static bool IsSystem(ClassDeclarationSyntax c)
        {
            var baseClass = c.BaseList != null ? c.BaseList.ChildNodes()?.ToArray() : new SyntaxNode[0];

            if (baseClass.Length == 0)
                return false;

            if (baseClass.Any(x => x.ToString().Contains(typeof(BaseSystem).Name)))
                return true;

            var gatherParents = classes.Where(x => baseClass.Any(z => z.ToString() == x.Identifier.ValueText));

            foreach (var parent in gatherParents)
            {
                if (IsSystem(parent))
                    return true;
            }

            return false;
        }

        class ClassVirtualizationVisitor : CSharpSyntaxRewriter
        {
            public ClassVirtualizationVisitor()
            {
                Classes = new List<ClassDeclarationSyntax>();
            }

            public List<ClassDeclarationSyntax> Classes { get; set; }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                node = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                Classes.Add(node); // save your visited classes
                return node;
            }
        }

        class StructVirtualizationVisitor : CSharpSyntaxRewriter
        {
            public StructVirtualizationVisitor()
            {
                Structs = new List<StructDeclarationSyntax>();
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
                Interfaces = new List<InterfaceDeclarationSyntax>();
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
}
