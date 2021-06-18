﻿using HECSFramework.Core.Helpers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
        public List<ClassDeclarationSyntax> needResolver = new List<ClassDeclarationSyntax>();
        public List<ClassDeclarationSyntax> containersSolve = new List<ClassDeclarationSyntax>();
        public List<Type> commands = new List<Type>();
        public const string Resolver = "Resolver";
        public const string Cs = ".cs";
        private string ResolverContainer = "ResolverDataContainer";
        public const string BluePrint = "BluePrint";

        #region SystemsBinding
        public string GetSystemBindsByRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var bindSystemFunc = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Commands", 1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class RegisterService"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(bindSystemFunc);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            bindSystemFunc.Add(new TabSimpleSyntax(2, "partial void BindSystem(ISystem system)"));
            bindSystemFunc.Add(new LeftScopeSyntax(2));
            bindSystemFunc.Add(GetGlobalBindingsRoslyn());
            bindSystemFunc.Add(GetLocalBindingsRoslyn());
            bindSystemFunc.Add(new RightScopeSyntax(2));

            return tree.ToString();
        }

        private ISyntax GetGlobalBindingsRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < Program.globalCommands.Count; i++)
            {
                var t = Program.globalCommands[i];

                if (i != 0)
                    tree.Add(new ParagraphSyntax());

                var name = t.Identifier.ValueText;
                tree.Add(new TabSimpleSyntax(3, $"if (system is IReactGlobalCommand<{name}> {name}GlobalCommandsReact)"));
                tree.Add(new TabSimpleSyntax(4, $"system.Owner.World.AddGlobalReactCommand<{name}>(system, {name}GlobalCommandsReact.CommandGlobalReact);"));
            }

            return tree;
        }

        private ISyntax GetLocalBindingsRoslyn()
        {
            var tree = new TreeSyntaxNode();

            var localSystemBind = Program.localCommands;

            for (int i = 0; i < localSystemBind.Count; i++)
            {
                var t = localSystemBind[i];
                var name = t.Identifier.ValueText;

                tree.Add(new ParagraphSyntax());
                tree.Add(new TabSimpleSyntax(3, $"if (system is IReactCommand<{name}> {name}CommandsReact)"));
                tree.Add(new TabSimpleSyntax(4, $"system.Owner.EntityCommandService.AddListener<{name}>(system, {name}CommandsReact.CommandReact);"));
            }

            return tree;
        }
        #endregion

        #region GenerateTypesMap
        public string GenerateTypesMapRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var componentsSegment = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("System.Collections.Generic"));
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("System", 1));
            tree.Add(new UsingSyntax("Systems", 1));

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(1), new SimpleSyntax($"public partial class {typeof(TypesProvider).Name}"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, "public TypesProvider()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, $"Count = {Program.componentsDeclarations.Count + 1};"));
            tree.Add(new TabSimpleSyntax(3, $"MapIndexes = GetMapIndexes();"));
            tree.Add(new TabSimpleSyntax(3, $"TypeToComponentIndex = GetTypeToComponentIndexes();"));
            tree.Add(new TabSimpleSyntax(3, $"HashToType = GetHashToTypeDictionary();"));
            tree.Add(new TabSimpleSyntax(3, $"TypeToHash = GetTypeToHash();"));
            tree.Add(new TabSimpleSyntax(3, $"HECSFactory = new HECSFactory();"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(GetTypeToComponentIndexesRoslyn());
            tree.Add(GetTypeToHashRoslyn());
            tree.Add(GetHashToTypeDictionaryRoslyn());

            //dictionary
            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax("private Dictionary<int, ComponentMaskAndIndex> GetMapIndexes()")));
            tree.Add(new ParagraphSyntax());
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "return new Dictionary<int, ComponentMaskAndIndex>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(componentsSegment);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(GetHECSComponentFactoryRoslyn());
            tree.Add(new RightScopeSyntax());

            //default stroke in dictionary
            componentsSegment.Add(new CompositeSyntax(new TabSpaceSyntax(4),
                new SimpleSyntax(@"{ -1, new ComponentMaskAndIndex {  ComponentName = ""DefaultEmpty"", ComponentsMask = HECSMask.Empty }},")));
            componentsSegment.Add(new ParagraphSyntax());

            //here we know how much mask field we have
            var m = ComponentsCountRoslyn();

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                var c = Program.componentsDeclarations[i];
                var index = i;
                componentsSegment.Add(GetComponentForTypeMapRoslyn(index, m, c));
            }

            return tree.ToString();
        }

        private ISyntax GetHECSComponentFactoryRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var constructor = new TreeSyntaxNode();
            var getComponentFunc = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class HECSFactory"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(constructor);
            tree.Add(getComponentFunc);
            tree.Add(new RightScopeSyntax(1));

            constructor.Add(new TabSimpleSyntax(2, "public HECSFactory()"));
            constructor.Add(new LeftScopeSyntax(2));
            constructor.Add(new TabSimpleSyntax(3, "getComponentFromFactoryByHash = GetComponentFromFactoryFunc;"));
            constructor.Add(new TabSimpleSyntax(3, "getSystemFromFactoryByHash = GetSystemFromFactoryFunc;"));
            constructor.Add(new RightScopeSyntax(2));

            getComponentFunc.Add(new ParagraphSyntax());
            getComponentFunc.Add(new TabSimpleSyntax(2, "private IComponent GetComponentFromFactoryFunc(int hashCodeType)"));
            getComponentFunc.Add(new LeftScopeSyntax(2));
            getComponentFunc.Add(new TabSimpleSyntax(3, "switch (hashCodeType)"));
            getComponentFunc.Add(new LeftScopeSyntax(3));
            getComponentFunc.Add(GetComponentsByHashCodeRoslyn());
            getComponentFunc.Add(new RightScopeSyntax(3));
            getComponentFunc.Add(new ParagraphSyntax());
            getComponentFunc.Add(new TabSimpleSyntax(3, "return default;"));
            getComponentFunc.Add(new RightScopeSyntax(2));

            getComponentFunc.Add(new ParagraphSyntax());
            getComponentFunc.Add(new TabSimpleSyntax(2, "private ISystem GetSystemFromFactoryFunc(int hashCodeType)"));
            getComponentFunc.Add(new LeftScopeSyntax(2));
            getComponentFunc.Add(new TabSimpleSyntax(3, "switch (hashCodeType)"));
            getComponentFunc.Add(new LeftScopeSyntax(3));
            getComponentFunc.Add(GetSystemsByHashCodeRoslyn());
            getComponentFunc.Add(new RightScopeSyntax(3));
            getComponentFunc.Add(new ParagraphSyntax());
            getComponentFunc.Add(new TabSimpleSyntax(3, "return default;"));
            getComponentFunc.Add(new RightScopeSyntax(2));

            return tree;
        }

        private ISyntax GetComponentsByHashCodeRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                if (i > 0)
                    tree.Add(new ParagraphSyntax());

                var component = Program.componentsDeclarations[i];

                tree.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(component.Identifier.ValueText)}:"));
                tree.Add(new TabSimpleSyntax(5, $"return new {component.Identifier.ValueText}();"));
            }

            return tree;
        }

        private ISyntax GetSystemsByHashCodeRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < Program.systemsDeclarations.Count; i++)
            {
                if (i > 0)
                    tree.Add(new ParagraphSyntax());

                var system = Program.systemsDeclarations[i];

                tree.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(system.Identifier.ValueText)}:"));
                tree.Add(new TabSimpleSyntax(5, $"return new {system.Identifier.ValueText}();"));
            }

            return tree;
        }

        private ISyntax GetHashToTypeDictionaryRoslyn()
        {
            var tree = new TreeSyntaxNode();

            var dicBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private Dictionary<int, Type> GetHashToTypeDictionary()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "return new Dictionary<int, Type>()"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(dicBody);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                var hash = IndexGenerator.GetIndexForType(Program.componentsDeclarations[i].Identifier.ValueText);
                dicBody.Add(new TabSimpleSyntax(4, $"{{ {hash}, typeof({Program.componentsDeclarations[i].Identifier.ValueText})}},"));
            }

            return tree;
        }

        private ISyntax GetTypeToComponentIndexesRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var dicBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private Dictionary<Type, int> GetTypeToComponentIndexes()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "return new Dictionary<Type, int>()"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(dicBody);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({Program.componentsDeclarations[i].Identifier.ValueText}), {i} }},"));
            }

            return tree;
        }

        private ISyntax GetTypeToHashRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var dicBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private Dictionary<Type, int> GetTypeToHash()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "return new Dictionary<Type, int>()"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(dicBody);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                var hash = IndexGenerator.GetIndexForType(Program.componentsDeclarations[i].Identifier.ValueText);
                dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({Program.componentsDeclarations[i].Identifier.ValueText}), {hash} }},"));
            }

            return tree;
        }

        private ISyntax GetComponentForTypeMapRoslyn(int index, int fieldCount, Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c)
        {
            var composite = new TreeSyntaxNode();
            var MaskPart = new TreeSyntaxNode();
            var maskBody = new TreeSyntaxNode();

            composite.Add(new ParagraphSyntax());
            composite.Add(new TabSpaceSyntax(3));
            composite.Add(new SimpleSyntax(CParse.LeftScope));
            composite.Add(new CompositeSyntax(new SimpleSyntax(CParse.Space + IndexGenerator.GetIndexForType(c.Identifier.ValueText).ToString() + CParse.Comma)));
            composite.Add(new SimpleSyntax($" new ComponentMaskAndIndex {{ComponentName = {CParse.Quote}{ c.Identifier.ValueText }{(CParse.Quote)}, ComponentsMask = new {typeof(HECSMask).Name}"));
            composite.Add(new ParagraphSyntax());
            composite.Add(MaskPart);
            composite.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax("}},")));
            composite.Add(new ParagraphSyntax());

            MaskPart.Add(new LeftScopeSyntax(4));
            MaskPart.Add(maskBody);
            MaskPart.Add(new RightScopeSyntax(4));

            var maskSplitToArray = CalculateIndexesForMaskRoslyn(index, fieldCount);

            maskBody.Add(new TabSimpleSyntax(5, $"Index = {index},"));

            for (int i = 0; i < fieldCount; i++)
            {
                if (maskSplitToArray[fieldCount - 1] > 1 && i < fieldCount - 1)
                {
                    maskBody.Add(new CompositeSyntax(new TabSpaceSyntax(5), new SimpleSyntax($"Mask0{i + 1} = 1ul << {0},")));
                    maskBody.Add(new ParagraphSyntax());
                    continue;
                }

                maskBody.Add(new CompositeSyntax(new TabSpaceSyntax(5), new SimpleSyntax($"Mask0{i + 1} = 1ul << {maskSplitToArray[i]},")));

                if (i > fieldCount - 1)
                    continue;

                maskBody.Add(new ParagraphSyntax());
            }

            return composite;
        }

        public int[] CalculateIndexesForMaskRoslyn(int index, int fieldCounts)
        {
            var t = new List<int>(new int[fieldCounts + 1]);

            var calculate = index;

            for (int i = 0; i < fieldCounts; i++)
            {
                if (calculate + 2 > 63)
                {
                    t[i] = 63;
                    calculate -= 61;
                    continue;
                }

                if (calculate < 63 && calculate >= 0)
                {
                    t[i] = calculate + 2;
                    calculate -= 100;
                    continue;
                }

                else if (calculate < 0)
                {
                    t[i] = 0;
                }
            }

            return t.ToArray();
        }
        #endregion

        #region HECSMasks
        public string GenerateHecsMasksRoslyn()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public static partial class HMasks"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(GetHecsMasksFieldsRoslyn());
            tree.Add(GetHecsMasksConstructorRoslyn());
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }

        private ISyntax GetNewComponentSolvedRoslyn(ClassDeclarationSyntax c, int index, int fieldCount)
        {
            var tree = new TreeSyntaxNode();
            var maskBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(4, $"new {typeof(HECSMask).Name}"));
            tree.Add(new LeftScopeSyntax(4));
            tree.Add(maskBody);
            tree.Add(new RightScopeSyntax(4, true));

            maskBody.Add(new TabSimpleSyntax(5, $"Index = {index},"));

            var maskSplitToArray = CalculateIndexesForMask(index, fieldCount);

            for (int i = 0; i < fieldCount; i++)
            {
                if (maskSplitToArray[fieldCount - 1] > 1 && i < fieldCount - 1)
                {
                    maskBody.Add(new CompositeSyntax(new TabSpaceSyntax(5), new SimpleSyntax($"Mask0{i + 1} = 1ul << {0},")));
                    maskBody.Add(new ParagraphSyntax());
                    continue;
                }

                maskBody.Add(new CompositeSyntax(new TabSpaceSyntax(5), new SimpleSyntax($"Mask0{i + 1} = 1ul << {maskSplitToArray[i]},")));

                if (i > fieldCount - 1)
                    continue;

                maskBody.Add(new ParagraphSyntax());
            }

            return tree;
        }

        private ISyntax GetHecsMasksConstructorRoslyn()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "static HMasks()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(GetHMaskBodyRoslyn());
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }

        private ISyntax GetHMaskBodyRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                var className = Program.componentsDeclarations[i].Identifier.ValueText.ToLower();
                var classType = Program.componentsDeclarations[i];
                var hash = IndexGenerator.GetIndexForType(classType.Identifier.ValueText);
                tree.Add(new TabSimpleSyntax(4, $"{className} = {GetNewComponentSolvedRoslyn(classType, i, ComponentsCountRoslyn())}"));
            }

            return tree;
        }

        private string GetHECSMaskNameRoslyn()
        {
            return typeof(HECSMask).Name;
        }

        private ISyntax GetHecsMasksFieldsRoslyn()
        {
            var tree = new TreeSyntaxNode();

            var hecsMaskname = typeof(HECSMask).Name;

            for (int i = 0; i < Program.componentsDeclarations.Count; i++)
            {
                tree.Add(new TabSimpleSyntax(2, $"private static {hecsMaskname} {Program.componentsDeclarations[i].Identifier.ValueText.ToLower()};"));
                tree.Add(new TabSimpleSyntax(2, $"public static ref {hecsMaskname} {Program.componentsDeclarations[i].Identifier.ValueText} => ref {Program.componentsDeclarations[i].Identifier.ValueText.ToLower()};"));
            }

            return tree;
        }
        #endregion

        #region ComponentContext
        public string GetComponentContextRoslyn()
        {
            var overTree = new TreeSyntaxNode();
            var entityExtention = new TreeSyntaxNode();

            var usings = new TreeSyntaxNode();
            var nameSpaces = new List<string>();

            var tree = new TreeSyntaxNode();
            var properties = new TreeSyntaxNode();

            var disposable = new TreeSyntaxNode();
            var disposableBody = new TreeSyntaxNode();


            var switchAdd = new TreeSyntaxNode();
            var switchBody = new TreeSyntaxNode();

            var switchRemove = new TreeSyntaxNode();
            var switchRemoveBody = new TreeSyntaxNode();

            overTree.Add(tree);
            overTree.Add(entityExtention);

            tree.Add(usings);
            tree.Add(new ParagraphSyntax());

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(1), new SimpleSyntax("public partial class ComponentContext : IDisposable"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(properties);
            tree.Add(switchAdd);
            tree.Add(switchRemove);
            tree.Add(disposable);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            switchAdd.Add(new ParagraphSyntax());
            switchAdd.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                new SimpleSyntax("partial void Add(IComponent component)"), new ParagraphSyntax()));
            switchAdd.Add(new LeftScopeSyntax(2));

            switchAdd.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax("switch (component)"), new ParagraphSyntax()));
            switchAdd.Add(new LeftScopeSyntax(3));
            switchAdd.Add(switchBody);
            switchAdd.Add(new RightScopeSyntax(3));
            switchAdd.Add(new RightScopeSyntax(2));

            switchAdd.Add(new ParagraphSyntax());
            switchAdd.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                new SimpleSyntax("partial void Remove(IComponent component)"), new ParagraphSyntax()));
            switchAdd.Add(new LeftScopeSyntax(2));

            switchAdd.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax("switch (component)"), new ParagraphSyntax()));
            switchAdd.Add(new LeftScopeSyntax(3));
            switchAdd.Add(switchRemoveBody);
            switchAdd.Add(new RightScopeSyntax(3));
            switchAdd.Add(new RightScopeSyntax(2));

            foreach (var c in Program.componentsDeclarations)
            {
                var name = c.Identifier.ValueText;

                properties.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                    new SimpleSyntax($"public {name} Get{name} {{ get; private set; }}"), new ParagraphSyntax()));

                var cArgument = name;
                var fixedArg = char.ToLower(cArgument[0]) + cArgument.Substring(1);

                switchBody.Add(new CompositeSyntax(new TabSpaceSyntax(4), new SimpleSyntax($"case {name} {fixedArg}:"), new ParagraphSyntax()));
                switchBody.Add(new LeftScopeSyntax(5));
                switchBody.Add(new CompositeSyntax(new TabSpaceSyntax(6), new SimpleSyntax($"Get{name} = {fixedArg};"), new ParagraphSyntax()));
                switchBody.Add(new CompositeSyntax(new TabSpaceSyntax(6), new ReturnSyntax()));
                switchBody.Add(new RightScopeSyntax(5));

                switchRemoveBody.Add(new CompositeSyntax(new TabSpaceSyntax(4), new SimpleSyntax($"case {name} {fixedArg}:"), new ParagraphSyntax()));
                switchRemoveBody.Add(new LeftScopeSyntax(5));
                switchRemoveBody.Add(new CompositeSyntax(new TabSpaceSyntax(6), new SimpleSyntax($"Get{name} = null;"), new ParagraphSyntax()));
                switchRemoveBody.Add(new CompositeSyntax(new TabSpaceSyntax(6), new ReturnSyntax()));
                switchRemoveBody.Add(new RightScopeSyntax(5));

                disposableBody.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax($"Get{name} = null;"), new ParagraphSyntax()));
                //if (c != componentTypes.Last())
                //    switchBody.Add(new ParagraphSyntax());

                var t = c.SyntaxTree.GetRoot().ChildNodes();

                foreach (var cn in t)
                {
                    if (cn is NamespaceDeclarationSyntax declarationSyntax)
                    {
                        nameSpaces.AddOrRemoveElement(declarationSyntax.Name.ToString(), true);
                    }
                }
            }

            nameSpaces.AddOrRemoveElement("Components", true);

            foreach (var n in nameSpaces)
                usings.Add(new UsingSyntax(n));

            AddEntityExtentionRoslyn(entityExtention);

            usings.Add(new UsingSyntax("System", 1));
            usings.Add(new UsingSyntax("System.Runtime.CompilerServices", 1));


            disposable.Add(new ParagraphSyntax());
            disposable.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax("public void Dispose()"), new ParagraphSyntax()));
            disposable.Add(new LeftScopeSyntax(2));
            disposable.Add(disposableBody);
            disposable.Add(new RightScopeSyntax(2));


            return overTree.ToString();
        }

        private void AddEntityExtentionRoslyn(TreeSyntaxNode tree)
        {
            var body = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(1), new SimpleSyntax("public static class EntityComponentExtentions"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            foreach (var c in Program.componentsDeclarations)
            {
                var name = c.Identifier.ValueText;

                body.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax(AggressiveInline), new ParagraphSyntax()));
                body.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                    new SimpleSyntax($"public static {name} Get{name}(this IEntity entity)"), new ParagraphSyntax()));
                body.Add(new LeftScopeSyntax(2));
                body.Add(new CompositeSyntax(new TabSpaceSyntax(3),
                    new SimpleSyntax($"return entity.ComponentContext.Get{name};"), new ParagraphSyntax()));
                body.Add(new RightScopeSyntax(2));

                if (c != Program.componentsDeclarations.Last())
                    body.Add(new ParagraphSyntax());
            }
        }

        #endregion

        #region GenerateComponentMask
        public string GenerateMaskProviderRoslyn()
        {
            var className = typeof(MaskProvider).Name;
            var hecsMaskname = typeof(HECSMask).Name;

            var hecsMaskPart = new TreeSyntaxNode();

            var componentsPeriodCount = ComponentsCountRoslyn();


            //overallType
            var tree = new TreeSyntaxNode();

            //defaultMask
            var maskFunc = new TreeSyntaxNode();
            var maskDefault = new TreeSyntaxNode();

            var fields = new TreeSyntaxNode();
            var operatorPlus = new TreeSyntaxNode();
            var operatorMinus = new TreeSyntaxNode();
            var isHaveBody = new TreeSyntaxNode();

            var equalityBody = new TreeSyntaxNode();
            var getHashCodeBody = new TreeSyntaxNode();

            var maskClassConstructor = new TreeSyntaxNode();

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(new TabSimpleSyntax(1, $"public partial class {className}"));
            tree.Add(new LeftScopeSyntax(1));

            //constructor
            tree.Add(new TabSimpleSyntax(2, "public MaskProvider()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(maskClassConstructor);
            tree.Add(new RightScopeSyntax(2));

            //Get Empty Mask
            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(maskFunc));
            maskFunc.Add(new TabSimpleSyntax(2, "public HECSMask GetEmptyMaskFunc()"));
            maskFunc.Add(new LeftScopeSyntax(2));
            maskFunc.Add(new TabSimpleSyntax(3, "return new HECSMask"));
            maskFunc.Add(new LeftScopeSyntax(3));
            maskFunc.Add(maskDefault);
            maskDefault.Add(new TabSimpleSyntax(4, "Index = -999,"));
            maskFunc.Add(new RightScopeSyntax(3, true));
            maskFunc.Add(new RightScopeSyntax(2));

            //plus operator
            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSimpleSyntax(2, $"public {hecsMaskname} GetPlusFunc({hecsMaskname} l, {hecsMaskname} r)")));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSimpleSyntax(3, $"return new {hecsMaskname}")));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(operatorPlus);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax($"public {hecsMaskname} GetMinusFunc({hecsMaskname} l, {hecsMaskname} r)"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax($"return new {hecsMaskname}"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(operatorMinus);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));

            //Equal part
            tree.Add(new ParagraphSyntax());
            tree.Add(EqualMaskRoslyn(equalityBody));

            //HashCodePart part
            tree.Add(new ParagraphSyntax());
            tree.Add(GetHashCodeRoslyn(getHashCodeBody));

            //bool IsHave
            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax($"public bool ContainsFunc(ref {hecsMaskname} original, ref {hecsMaskname} other)"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(isHaveBody);
            tree.Add(new SimpleSyntax(CParse.Semicolon));
            isHaveBody.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax(CParse.Return), new SpaceSyntax()));
            tree.Add(new ParagraphSyntax());
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(HecsMaskPartRoslyn(hecsMaskPart));
            tree.Add(new RightScopeSyntax());

            //costructor for mask provider class
            maskClassConstructor.Add(GetMaskProviderConstructorBodyRoslyn());

            //fill trees
            for (int i = 0; i < ComponentsCountRoslyn(); i++)
            {
                maskDefault.Add(new CompositeSyntax(new TabSpaceSyntax(4), new SimpleSyntax($"Mask0{i + 1} = 0,"), new ParagraphSyntax()));
                equalityBody.Add(new SimpleSyntax($"{CParse.Space}&& mask.Mask0{i + 1} == otherMask.Mask0{i + 1}"));
                fields.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax($"public ulong Mask0{i + 1};"), new ParagraphSyntax()));
                operatorPlus.Add(new CompositeSyntax(new TabSpaceSyntax(4), new SimpleSyntax($"Mask0{i + 1} = l.Mask0{i + 1} | r.Mask0{i + 1},"), new ParagraphSyntax()));
                operatorMinus.Add(new CompositeSyntax(new TabSpaceSyntax(4), new SimpleSyntax($"Mask0{i + 1} = l.Mask0{i + 1} ^ r.Mask0{i + 1},"), new ParagraphSyntax()));
                getHashCodeBody.Add(new TabSimpleSyntax(4, $"hash += ({(i + 1) * 3} * mask.Mask0{i + 1}.GetHashCode());"));

                if (i == 0)
                    isHaveBody.Add(new SimpleSyntax($"(original.Mask0{i + 1} & other.Mask0{i + 1}) != 0"));
                else
                    isHaveBody.Add(new CompositeSyntax(new ParagraphSyntax(), new TabSpaceSyntax(6),
                        new SimpleSyntax("&&"), new SimpleSyntax($"(original.Mask0{i + 1} & other.Mask0{i + 1}) != 0")));

                if (i > 0)
                    hecsMaskPart.Add(new TabSimpleSyntax(2, $"public ulong Mask0{i + 1};"));
            }

            return tree.ToString();
        }

        private ISyntax HecsMaskPartRoslyn(ISyntax body)
        {
            var tree = new TreeSyntaxNode();
            var maskType = typeof(HECSMask).Name;

            //tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            //tree.Add(new LeftScopeSyntax());
            tree.Add(new ParagraphSyntax());
            tree.Add(new SimpleSyntax("#pragma warning disable" + CParse.Paragraph));
            tree.Add(new TabSimpleSyntax(1, $"public partial struct {maskType}"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new SimpleSyntax("#pragma warning enable" + CParse.Paragraph));
            return tree;
        }

        private ISyntax GetHashCodeRoslyn(ISyntax body)
        {
            var tree = new TreeSyntaxNode();
            var maskType = typeof(HECSMask).Name;
            tree.Add(new TabSimpleSyntax(2, $"public int GetHashCodeFunc(ref {maskType} mask)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "unchecked"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, "int hash = 256;"));
            tree.Add(body);
            tree.Add(new TabSimpleSyntax(4, "return hash;"));
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new RightScopeSyntax(2));
            return tree;
        }

        private ISyntax EqualMaskRoslyn(ISyntax body)
        {
            var tree = new TreeSyntaxNode();
            var maskSyntax = typeof(HECSMask).Name;

            tree.Add(new TabSimpleSyntax(2, $"public bool GetEqualityOfMasksFunc(ref {maskSyntax} mask, object other)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax($"return other is {maskSyntax} otherMask")));
            tree.Add(body);
            tree.Add(new SimpleSyntax(CParse.Semicolon));
            tree.Add(new ParagraphSyntax());
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }

        private ISyntax GetMaskProviderConstructorBodyRoslyn()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(3, "GetPlus = GetPlusFunc;"));
            tree.Add(new TabSimpleSyntax(3, "GetMinus = GetMinusFunc;"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(3, "Empty = GetEmptyMaskFunc;"));
            tree.Add(new TabSimpleSyntax(3, "Contains = ContainsFunc;"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(3, "GetMaskIsEqual = GetEqualityOfMasksFunc;"));
            tree.Add(new TabSimpleSyntax(3, "GetMaskHashCode = GetHashCodeFunc;"));

            return tree;
        }
        #endregion

        #region EntityExtentions
        private string GenerateEntityExtentionsRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var methods = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("Components"));
            tree.Add(new ParagraphSyntax());

            tree.Add(new UsingSyntax("System.Runtime.CompilerServices"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new ParagraphSyntax());

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(1), new SimpleSyntax("public static class EntityGenericExtentions"), new ParagraphSyntax()));

            tree.Add(new LeftScopeSyntax(1, true));
            tree.Add(methods);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax(0));

            foreach (var c in Program.componentsDeclarations)
            {
                EntityAddComponentRoslyn(c, methods);
                EntityGetComponentRoslyn(c, methods);
                EntityRemoveComponentRoslyn(c, methods);
            }

            return tree.ToString();
        }

        private void EntityAddComponentRoslyn(ClassDeclarationSyntax component, TreeSyntaxNode tree)
        {
            var name = component.Identifier.ValueText;

            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax(AggressiveInline)));
            tree.Add(new ParagraphSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                new SimpleSyntax($"public static void Add{name}(this Entity entity, {name} {name.ToLower()}Component)")));

            tree.Add(new ParagraphSyntax());
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(3),
                new SimpleSyntax($"EntityManager.World(entity.WorldIndex).Add{name}Component({name.ToLower()}Component, ref entity);"),
                new ParagraphSyntax()));

            tree.Add(new RightScopeSyntax(2));
        }

        private void EntityGetComponentRoslyn(ClassDeclarationSyntax component, TreeSyntaxNode tree)
        {
            var name = component.Identifier.ValueText;

            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax(AggressiveInline)));
            tree.Add(new ParagraphSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                new SimpleSyntax($"public static ref {name} Get{name}(this ref Entity entity)")));

            tree.Add(new ParagraphSyntax());
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(3),
                new SimpleSyntax($"return ref EntityManager.World(entity.WorldIndex).Get{name}Component(ref entity);"),
                new ParagraphSyntax()));

            tree.Add(new RightScopeSyntax(2));
        }

        private void EntityRemoveComponentRoslyn(ClassDeclarationSyntax component, TreeSyntaxNode tree)
        {
            var name = component.Identifier.ValueText;

            tree.Add(new ParagraphSyntax());
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2), new SimpleSyntax(AggressiveInline)));
            tree.Add(new ParagraphSyntax());

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(2),
                new SimpleSyntax($"public static void Remove{name}(this ref Entity entity)")));

            tree.Add(new ParagraphSyntax());
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(3),
                new SimpleSyntax($"EntityManager.World(entity.WorldIndex).Remove{name}Component(ref entity);"),
                new ParagraphSyntax()));

            tree.Add(new RightScopeSyntax(2));
        }
        #endregion

        #region Resolvers
        public List<(string name, string content)> GetSerializationResolvers()
        {
            var list = new List<(string, string)>();

            foreach (var c in Program.componentsDeclarations)
            {
                var needContinue = false;
                var neededClasses = Program.classes.Where(x => x.Identifier.ValueText == c.Identifier.ValueText);
                var attr2 = neededClasses.SelectMany(x => x.AttributeLists).ToList();

                if (attr2 != null)
                {
                    foreach (var attributeList in attr2)
                    {
                        foreach (var a in attributeList.Attributes)
                        {
                            if (a.Name.ToString() == "CustomResolver")
                            {
                                containersSolve.Add(c);
                                needContinue = true;
                                break;
                            }
                        }
                    }
                }

                if (needContinue)
                    continue;

                containersSolve.Add(c);
                needResolver.Add(c);
            }


            foreach (var c in needResolver)
            {
                list.Add((c.Identifier.ValueText + Resolver + Cs, GetResolver(c).ToString()));
            }

            return list;
        }

        private ISyntax GetResolver(ClassDeclarationSyntax c)
        {
            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();
            var fields = new TreeSyntaxNode();
            var constructor = new TreeSyntaxNode();
            var defaultConstructor = new TreeSyntaxNode();
            var outFunc = new TreeSyntaxNode();
            var out2EntityFunc = new TreeSyntaxNode();

            var name = c.Identifier.ValueText;
            
            tree.Add(usings);
            usings.Add(new UsingSyntax("Components"));
            usings.Add(new UsingSyntax("System"));
            usings.Add(new UsingSyntax("MessagePack"));

            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "[MessagePackObject]"));
            tree.Add(new TabSimpleSyntax(1, $"public struct {name + Resolver} : IResolver<{name}>, IData"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(fields);
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public {name + Resolver} In(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(constructor);
            tree.Add(new RightScopeSyntax(2));
            //tree.Add(new ParagraphSyntax());
            //tree.Add(defaultConstructor);
            //tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public void Out(ref {typeof(IEntity).Name} entity)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(GetOutToEntityVoidBodyRoslyn(c));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new TabSimpleSyntax(2, $"public void Out(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(outFunc);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            //((c.Members.ToArray()[0] as FieldDeclarationSyntax).AttributeLists.ToArray()[0].Attributes.ToArray()[0] as AttributeSyntax).ArgumentList.Arguments.ToArray()[0].ToString()
            var typeFields = new List<GatheredField>(128);
            List<(string type, string name)> fieldsForConstructor = new List<(string type, string name)>();

            foreach (var m in c.Members)
            {
               

                if (m is FieldDeclarationSyntax field)
                {
                    var validate = IsValidField(field);

                    if (validate.valid)
                    {
                        var getNameSpace = GetNameSpace(field);
                        var getListNameSpace = GetListNameSpace(field);

                        if (getListNameSpace != string.Empty)
                            AddUniqueSyntax(usings, new UsingSyntax(getListNameSpace));

                        if (getNameSpace != string.Empty)
                            AddUniqueSyntax(usings, new UsingSyntax(getNameSpace));

                        typeFields.Add(new GatheredField
                        {
                            Order = validate.Order,
                            Type = field.Declaration.Type.ToString(),
                            FieldName = field.Declaration.Variables[0].Identifier.ToString(),
                            Node = field
                        });
                    }
                }

                if (m is PropertyDeclarationSyntax property)
                {
                    var validate = IsValidProperty(property);

                    if (validate.valid)
                    {
                        

                        typeFields.Add(new GatheredField
                        {
                            Order = validate.Order,
                            Type = property.Type.ToString(),
                            FieldName = property.Identifier.ToString(), 
                            Node = property
                        });
                    }
                }
            }

            foreach (var f in typeFields)
            {
                
                    fields.Add(new TabSimpleSyntax(2, $"[Key({f.Order})]"));
                    fields.Add(new TabSimpleSyntax(2, $"public {f.Type} {f.FieldName};"));

                    fieldsForConstructor.Add((f.Type, f.FieldName));

                    constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = {c.Identifier.ValueText.ToLower()}.{f.FieldName};"));
                    outFunc.Add(new TabSimpleSyntax(3, $"{c.Identifier.ValueText.ToLower()}.{f.FieldName} = this.{f.FieldName};"));
            }

            if (c.BaseList.ChildNodes().Any(x => x is SimpleBaseTypeSyntax simple && simple.ToString().Contains("IAfterSerializationComponent")))
            {
                outFunc.Add(new TabSimpleSyntax(3, $"{c.Identifier.ValueText.ToLower()}.AfterSync();"));
            }

            ////defaultConstructor.Add(DefaultConstructor(c, fieldsForConstructor, fields, constructor));
            constructor.Add(new TabSimpleSyntax(3, "return this;"));

            usings.Add(new ParagraphSyntax());
            return tree;
        }

        public (bool valid, int Order) IsValidField(FieldDeclarationSyntax fieldDeclarationSyntax)
        {
            foreach (var a in fieldDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToArray())
            {
                if (a.ToString().Contains("Field") && fieldDeclarationSyntax.Modifiers.ToString().Contains("public"))
                {
                    if (a.ArgumentList == null)
                        continue;

                    var intValue = int.Parse(a.ArgumentList.Arguments.ToArray()[0].ToString());
                    Console.WriteLine("нашли что надо");
                    return (true, intValue);
                }
            }

            return (false, -1);
        }

        private string GetNameSpace(FieldDeclarationSyntax field)
        {
            var neededClass = Program.classes.FirstOrDefault(x => x.Identifier.ValueText == field.Declaration.Type.ToString());
            var namespaceString = string.Empty;

            if (neededClass == null)
                return namespaceString;

            var tree = neededClass.SyntaxTree.GetRoot().ChildNodes();

            foreach (var cn in tree)
            {
                if (cn is NamespaceDeclarationSyntax declarationSyntax)
                {
                    var namespaceName = declarationSyntax.Name.ToString();
                    namespaceString = namespaceName;
                    break;
                }
            }

            return namespaceString;
        }

        private string GetListNameSpace(FieldDeclarationSyntax field)
        {
            var namespaceString = string.Empty;

            if (field.Declaration.Type.ToString().Contains("List"))
                namespaceString = "System.Collections.Generic";

            return namespaceString;
        }

        public (bool valid, int Order) IsValidProperty(PropertyDeclarationSyntax property)
        {
            if (!property.Modifiers.ToString().Contains("public"))
                return (false, -1);

            if (property.Type.ToString().Contains("ReactiveValue"))
            {
            }
            else
            {
                if (property.AccessorList == null)
                    return (false, -1);

                var needed = property.AccessorList.Accessors.FirstOrDefault(x => x.Kind() == SyntaxKind.SetAccessorDeclaration);

                if (needed == null)
                    return (false, -1);

                if (needed.Modifiers.Any(x => x.Kind() == SyntaxKind.ProtectedKeyword || x.Kind() == SyntaxKind.PrivateKeyword))
                    return (false, -1);
            }

            foreach (var a in property.AttributeLists.SelectMany(x => x.Attributes).ToArray())
            {
                if (a.ToString().Contains("Field") && property.Modifiers.ToString().Contains("public"))
                {
                    var intValue = int.Parse(a.ArgumentList.Arguments.ToArray()[0].ToString());
                    Console.WriteLine("нашли что надо");
                    return (true, intValue);
                }
            }

            return (false, -1);
        }

        public struct GatheredField
        {
            public string Type;
            public string FieldName;
            public int Order;
            public CSharpSyntaxNode Node;
        }

        private ISyntax GetOutToEntityVoidBodyRoslyn(ClassDeclarationSyntax c)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(3, $"var local = entity.Get{c.Identifier.ValueText}();"));
            tree.Add(new TabSimpleSyntax(3, $"Out(ref local);"));
            return tree;
        }

        private ISyntax DefaultConstructor(Type type, List<(string type, string name)> data, ISyntax fields, ISyntax constructor)
        {
            var tree = new TreeSyntaxNode();
            var arguments = new TreeSyntaxNode();

            var defaultConstructor = new TreeSyntaxNode();
            var defaultconstructorSignature = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, $"[SerializationConstructor]"));
            tree.Add(defaultconstructorSignature);
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(defaultConstructor);
            tree.Add(new RightScopeSyntax(2));

            if (data.Count == 0)
            {
                fields.Tree.Add(IsTagBool());
                constructor.Tree.Add(new TabSimpleSyntax(3, "IsTag = false;"));
                defaultConstructor.Tree.Add(new TabSimpleSyntax(3, "IsTag = false;"));
                arguments.Add(new SimpleSyntax("bool isTag"));

                defaultconstructorSignature.Add(new TabSimpleSyntax(2, $"public {type.Name + Resolver}({arguments})"));
                return tree;
            }

            for (int i = 0; i < data.Count; i++)
            {
                (string type, string name) d = data[i];
                var needComma = i < data.Count - 1 ? CParse.Comma : "";

                arguments.Add(new SimpleSyntax($"{d.type} {d.name}{needComma}"));
                defaultConstructor.Add(new TabSimpleSyntax(3, $"this.{d.name} = {d.name};"));
            }

            defaultconstructorSignature.Add(new TabSimpleSyntax(2, $"public {type.Name + Resolver}({arguments})"));
            return tree;
        }

        private ISyntax IsTagBool()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(2, "[Key(0)]"));
            tree.Add(new TabSimpleSyntax(2, "public bool IsTag;"));
            return tree;
        }

        #endregion

        #region  ResolversMap
        public string GetResolverMap()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("HECSFramework.Core"));
            tree.Add(new UsingSyntax("MessagePack", 1));
            tree.Add(GetUnionResolvers());
            tree.Add(new ParagraphSyntax());
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(ResolverMapConstructor());
            tree.Add(LoadDataFromContainerSwitch());
            tree.Add(GetContainerForComponentFuncProvider());
            tree.Add(ProcessComponents());
            tree.Add(GetComponentFromContainerFuncRealisation());
            tree.Add(ProcessResolverContainerRealisation());
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());
            return tree.ToString();
        }

        private ISyntax GetUnionResolvers()
        {
            var tree = new TreeSyntaxNode();
            var unionPart = new TreeSyntaxNode();
            tree.Add(unionPart);
            tree.Add(new TabSimpleSyntax(0, "public partial interface IData { }"));

            for (int i = 0; i < containersSolve.Count; i++)
            {
                var name = containersSolve[i].Identifier.ValueText;
                unionPart.Add(new TabSimpleSyntax(0, $"[Union({i}, typeof({name}Resolver))]"));
            }

            return tree;
        }

        private ISyntax ProcessResolverContainerRealisation()
        {
            var tree = new TreeSyntaxNode();
            var caseBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private void ProcessResolverContainerRealisation(ref ResolverDataContainer dataContainerForResolving, ref IEntity entity)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (dataContainerForResolving.TypeHashCode)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(caseBody);
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new RightScopeSyntax(2));

            foreach (var container in containersSolve)
            {
                var name = container.Identifier.ValueText;
                caseBody.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(name)}:"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}{Resolver.ToLower()} = ({name + Resolver})dataContainerForResolving.Data;"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}component = ({name})entity.Get{name}();"));
                caseBody.Add(new TabSimpleSyntax(5, $"{name}{Resolver.ToLower()}.Out(ref {name}component);"));
                caseBody.Add(new TabSimpleSyntax(5, $"break;"));
            }

            return tree;
        }

        private ISyntax GetComponentFromContainerFuncRealisation()
        {
            var tree = new TreeSyntaxNode();
            var caseBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private IComponent GetComponentFromContainerFuncRealisation(ResolverDataContainer resolverDataContainer)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (resolverDataContainer.TypeHashCode)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(caseBody);
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, "return default;"));
            tree.Add(new RightScopeSyntax(2));

            foreach (var container in containersSolve)
            {
                var name = container.Identifier.ValueText;
                caseBody.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(name)}:"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}new = new {name}();"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}data = ({name + Resolver})(resolverDataContainer.Data);"));
                caseBody.Add(new TabSimpleSyntax(5, $"{name}data.Out(ref {name}new);"));
                caseBody.Add(new TabSimpleSyntax(5, $"return {name}new;"));
            }

            return tree;
        }

        private ISyntax ProcessComponents()
        {
            var tree = new TreeSyntaxNode();
            var caseBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private void ProcessComponents(ref {ResolverContainer} dataContainerForResolving, int worldIndex)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (dataContainerForResolving.TypeHashCode)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(caseBody);
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new RightScopeSyntax(2));

            foreach (var container in containersSolve)
            {
                var name = container.Identifier.ValueText;
                caseBody.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(name)}:"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}{Resolver.ToLower()} = ({name + Resolver})(dataContainerForResolving.Data);"));
                caseBody.Add(new TabSimpleSyntax(5, $"if (EntityManager.TryGetEntityByID(dataContainerForResolving.EntityGuid, out var entityOf{name}))"));
                caseBody.Add(new LeftScopeSyntax(5));
                caseBody.Add(new TabSimpleSyntax(6, $"var {name}component = ({name})entityOf{name}.Get{name}();"));
                caseBody.Add(new TabSimpleSyntax(6, $"{name}{Resolver.ToLower()}.Out(ref {name}component);"));
                caseBody.Add(new RightScopeSyntax(5));
                caseBody.Add(new TabSimpleSyntax(5, $"break;"));
            }

            return tree;
        }

        private ISyntax GetContainerForComponentFuncProvider()
        {
            var tree = new TreeSyntaxNode();
            var caseBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private {ResolverContainer} GetContainerForComponentFuncProvider<T>(T component) where T: IComponent"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (component.GetTypeHashCode)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(caseBody);
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(3, "return default;"));
            tree.Add(new RightScopeSyntax(2));

            foreach (var container in containersSolve)
            {
                var name = container.Identifier.ValueText;

                var lowerContainerName = (name + Resolver).ToLower();
                caseBody.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(name)}:"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {lowerContainerName} = component as {name};"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}Data = new {name + Resolver}().In(ref {lowerContainerName});"));
                caseBody.Add(new TabSimpleSyntax(5, $"return PackComponentToContainer(component, {name}Data);"));
            }

            return tree;
        }

        private ISyntax LoadDataFromContainerSwitch()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"partial void LoadDataFromContainerSwitch({"ResolverDataContainer"} dataContainerForResolving, int worldIndex)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (dataContainerForResolving.Type)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, "case 0:"));
            tree.Add(new TabSimpleSyntax(5, "ProcessComponents(ref dataContainerForResolving, worldIndex);"));
            tree.Add(new TabSimpleSyntax(5, "break;"));
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new RightScopeSyntax(2));
            return tree;
        }

        private ISyntax ResolverMapConstructor()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "public ResolversMap()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "GetComponentContainerFunc = GetContainerForComponentFuncProvider;"));
            tree.Add(new TabSimpleSyntax(3, "ProcessResolverContainer = ProcessResolverContainerRealisation;"));
            tree.Add(new TabSimpleSyntax(3, "GetComponentFromContainer = GetComponentFromContainerFuncRealisation;"));
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }
        #endregion

        #region BluePrintsProvider
        public string GetBluePrintsProvider()
        {
            var tree = new TreeSyntaxNode();
            var constructor = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("Systems"));
            tree.Add(new UsingSyntax("System.Collections.Generic", 1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Unity"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class BluePrintsProvider"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, "public BluePrintsProvider()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(constructor);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            constructor.Add(GetComponentsBluePrintsDictionary());
            constructor.Add(GetSystemsBluePrintsDictionary());

            return tree.ToString();
        }

        private ISyntax GetComponentsBluePrintsDictionary()
        {
            var tree = new TreeSyntaxNode();
            var dictionaryBody = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "Components = new Dictionary<Type, Type>"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(dictionaryBody);
            tree.Add(new RightScopeSyntax(2, true));

            foreach (var c in Program.componentsDeclarations)
            {
                var name = c.Identifier.ValueText;
                dictionaryBody.Add(new TabSimpleSyntax(3, $" {CParse.LeftScope} typeof({name}), typeof({name}{BluePrint}) {CParse.RightScope},"));
            }

            return tree;
        }

        private ISyntax GetSystemsBluePrintsDictionary()
        {
            var tree = new TreeSyntaxNode();
            var dictionaryBody = new TreeSyntaxNode();

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "Systems = new Dictionary<Type, Type>"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(dictionaryBody);
            tree.Add(new RightScopeSyntax(2, true));

            foreach (var s in Program.systemsDeclarations)
            {
                var name = s.Identifier.ValueText;
                dictionaryBody.Add(new TabSimpleSyntax(3, $" {CParse.LeftScope} typeof({name}), typeof({name}{BluePrint}) {CParse.RightScope},"));
            }

            return tree;
        }
        #endregion

        #region GenerateSystemsBluePrints
        public List<(string name, string classBody)> GenerateSystemsBluePrints()
        {
            var list = new List<(string name, string classBody)>();

            foreach (var c in Program.systemsDeclarations)
            {
                var name = c.Identifier.ValueText;
                list.Add((name + BluePrint + ".cs", GetSystemBluePrint(c)));
            }
                

            return list;
        }

        private string GetSystemBluePrint(ClassDeclarationSyntax type)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Systems", 1));

            tree.Add(new NameSpaceSyntax("HECSFramework.Unity"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, $"public class {type.Identifier.ValueText}{BluePrint} : SystemBluePrint<{type.Identifier.ValueText}>"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }

        #endregion

        #region GenerateComponentsBluePrints  
        public List<(string name, string classBody)> GenerateComponentsBluePrints()
        {
            var list = new List<(string name, string classBody)>();

            foreach (var c in Program.componentsDeclarations)
            {
                var name = c.Identifier.ValueText;
                list.Add((name + BluePrint + ".cs", GetComponentBluePrint(c)));
            }
                

            return list;
        }

        private string GetComponentBluePrint(ClassDeclarationSyntax type)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("System.Collections.Generic", 1));

            tree.Add(new NameSpaceSyntax("HECSFramework.Unity"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, $"public class {type.Identifier.ValueText}{BluePrint} : ComponentBluePrintContainer<{type.Identifier.ValueText}>"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }



        #endregion

        #region Helpers

        private void AddUniqueSyntax(ISyntax syntaxTo, ISyntax from)
        {
            if (syntaxTo.ToString().Contains(from.ToString()))
                return;

            syntaxTo.Tree.Add(from);
        }

        private int ComponentsCountRoslyn()
        {
            double count = Program.componentsDeclarations.Count;

            if (count == 0)
                ++count;

            var componentsPeriodCount = Math.Ceiling(count / 61);

            return (int)componentsPeriodCount;
        }
        #endregion
    }
}
