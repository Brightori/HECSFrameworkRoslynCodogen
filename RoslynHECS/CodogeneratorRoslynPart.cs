using System;
using System.Collections.Generic;
using System.Linq;
using HECSFramework.Core.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS;
using RoslynHECS.DataTypes;
using RoslynHECS.Helpers;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
        public HashSet<ClassDeclarationSyntax> needResolver = new HashSet<ClassDeclarationSyntax>();
        public List<ClassDeclarationSyntax> containersSolve = new List<ClassDeclarationSyntax>();
        public List<Type> commands = new List<Type>();
        public List<string> alrdyAtContext = new List<string>();
        public const string Resolver = "Resolver";
        public const string Cs = ".cs";
        private string ResolverContainer = "ResolverDataContainer";
        public const string BluePrint = "BluePrint";
        public const string ContextSetter = "ContextSetter";

        public const string SystemBindSetter = "SystemBindSetter";
        public const string ISystemSetter = "ISystemSetter";
        public const string SystemBindContainer = "BindContainerForSys";

        public const string IReactGlobalCommand = "IReactGlobalCommand";
        public const string INetworkComponent = "INetworkComponent";
        public const string IReactCommand = "IReactCommand";
        public const string IReactComponentLocal = "IReactComponentLocal";
        public const string IReactComponentGlobal = "IReactComponentGlobal";
        
        public const string IReactGenericGlobalComponent = "IReactGenericGlobalComponent";
        public const string IReactGenericLocalComponent = "IReactGenericLocalComponent";
        
        public const string CurrentSystem = "currentSystem";

        public const string IReactNetworkCommandGlobal = "IReactNetworkCommandGlobal";
        public const string IReactNetworkCommandLocal = "IReactNetworkCommandLocal";

        private HashSet<LinkedInterfaceNode> interfaceCache = new HashSet<LinkedInterfaceNode>(64);
        private HashSet<LinkedGenericInterfaceNode> interfaceGenericCache = new HashSet<LinkedGenericInterfaceNode>(64);
        private HashSet<ClassDeclarationSyntax> systemCasheParentsAndPartial = new HashSet<ClassDeclarationSyntax>(64);

        #region SystemsBinding
        public string GetSystemBindsByRoslyn()
        {
            var tree = new TreeSyntaxNode();
            var bindSystemFunc = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("Systems"));
            tree.Add(new UsingSyntax("Commands", 1));
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("System.Reflection"));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(GetContaineresForSystems());
            tree.Add(new TabSimpleSyntax(1, "public static partial class TypesMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(bindSystemFunc);
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            bindSystemFunc.Add(new TabSimpleSyntax(2, "static partial void SetSystemSetters()"));
            bindSystemFunc.Add(new LeftScopeSyntax(2));
            bindSystemFunc.Add(GetSystemsContainersDictionary());
            bindSystemFunc.Add(new RightScopeSyntax(2));

            return tree.ToString();
        }

        private ISyntax GetSystemsContainersDictionary()
        {
            var tree = new TreeSyntaxNode();
            var dicBody = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(3, $"systemsSetters = new System.Collections.Generic.Dictionary<Type, {ISystemSetter}>()"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(dicBody);
            tree.Add(new RightScopeSyntax(3, true));

            foreach (var s in Program.systemOverData.Values)
            {
                if (s.IsAbstract) continue;

                dicBody.Add(new TabSimpleSyntax(4, $"{CParse.LeftScope}typeof({s.Name}), new {s.Name}{SystemBindContainer}(){CParse.RightScope},"));
            }

            return tree;
        }

        /// <summary>
        /// тут мы получаем все контейнеры для систем
        /// </summary>
        /// <returns></returns>
        private ISyntax GetContaineresForSystems()
        {
            var tree = new TreeSyntaxNode();

            foreach (var system in Program.systemOverData)
            {
                if (system.Value.IsAbstract) continue;

                //собираем парт системы если они есть
                tree.Add(GetSystemContainer(system.Value, out var bindContainerBody, out var unbindContainer, out var systemPlace, out var fields));

                interfaceGenericCache.Clear();
                systemCasheParentsAndPartial.Clear();

                system.Value.GetGenericInterfaces(interfaceGenericCache);
                system.Value.GetAllParentsAndParts(systemCasheParentsAndPartial);

                foreach (var interfaceType in interfaceGenericCache)
                {
                    ProcessReacts(interfaceType, bindContainerBody, unbindContainer);
                }

                foreach (var systemPart in systemCasheParentsAndPartial)
                {
                    var attributes = systemPart.DescendantNodes().OfType<AttributeListSyntax>();

                    if (attributes != null)
                    {
                        foreach (var attribute in attributes)
                        {
                            if (attribute.Attributes.Any(x => x.ToString().Contains("Required")))
                            {
                                if (attribute.Parent is FieldDeclarationSyntax field)
                                {
                                    var types = field.DescendantNodes().OfType<IdentifierNameSyntax>();

                                    if (field.Modifiers.Any(x => x.ToString().Contains("private") || x.ToString().Contains("protected")))
                                    {
                                        SetPrivateComponentBinder(field, systemPart.Identifier.ValueText, fields, bindContainerBody, unbindContainer);
                                    }
                                    else
                                    {
                                        SetPublicComponentBinder(field, systemPart.Identifier.ValueText, bindContainerBody, unbindContainer);
                                    }
                                }
                            }
                        }
                    }
                }

                //если есть что биндить, добавляем каст системы к нужному типу
                if (bindContainerBody.Tree.Count > 0)
                    systemPlace.Tree.Add(new TabSimpleSyntax(3, $"var {CurrentSystem} = ({system.Value.Name})system;"));
            }

            return tree;
        }


        private void ProcessReacts(LinkedGenericInterfaceNode part, ISyntax bindContainerBody, ISyntax unbindContainer)
        {
            switch (part.BaseInterface.Name)
            {
                case IReactCommand:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"LocalCommandListener<{part.GenericType}>.AddListener(currentSystem.Owner.World.Index,{CurrentSystem});"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"LocalCommandListener<{part.GenericType}>.RemoveListener(currentSystem.Owner.WorldId, {CurrentSystem});"));
                    break;
                case IReactGlobalCommand:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactCommand<{part.GenericType}>(system, {CurrentSystem});"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.RemoveGlobalReactCommand<{part.GenericType}>({CurrentSystem});"));
                    break;
                case IReactComponentLocal:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, true);"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, false);"));
                    break;
                case IReactComponentGlobal:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactComponent<{part.GenericType}>({CurrentSystem}, true);"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactComponent<{part.GenericType}>({CurrentSystem}, false);"));
                    break;
                case IReactGenericLocalComponent:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalGenericReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, true);"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalGenericReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, false);"));
                    break;
                case IReactGenericGlobalComponent:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalGenericReactComponent<{part.GenericType}>({CurrentSystem}, true);"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalGenericReactComponent<{part.GenericType}>({CurrentSystem}, false);"));
                    break;
            }

            if (Program.CommandMapNeeded)
            {
                switch (part.BaseInterface.Name)
                {
                    case IReactNetworkCommandGlobal:
                        bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"GlobalNetworkCommandListener<{part.GenericType}>.AddListener(system.Owner.World.Index, currentSystem);"));
                        unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"GlobalNetworkCommandListener<{part.GenericType}>.RemoveListener(system.Owner.World.Index, currentSystem);"));
                        break;

                    case IReactNetworkCommandLocal:
                        bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"LocalNetworkCommandListener<{part.GenericType}>.AddListener(currentSystem.Owner.World.Index, currentSystem);"));
                        unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"LocalNetworkCommandListener<{part.GenericType}>.RemoveListener(currentSystem.Owner.World.Index, currentSystem);"));
                        break;
                }
            }
        }

        private void SetPrivateComponentBinder(FieldDeclarationSyntax fieldDeclaration, string system, ISyntax fields, ISyntax binder, ISyntax unbinder)
        {
            var findComponent = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.componentOverData.ContainsKey(x.ToString()));

            if (findComponent == null) return;

            var fieldType = findComponent.ToString();
            var fieldName = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is VariableDeclaratorSyntax).ToString();

            var fieldBindName = fieldName + "FieldBinding";

            fields.Tree.Add(new TabSimpleSyntax(2, $"private FieldInfo {fieldBindName} = typeof({system}).GetField({CParse.Quote}{fieldName}{CParse.Quote}, BindingFlags.Instance | BindingFlags.NonPublic);"));
            binder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue({CurrentSystem}, {CurrentSystem}.Owner.GetOrAddComponent<{fieldType}>());"));
            unbinder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue(system, null);"));
        }

        private void SetPublicComponentBinder(FieldDeclarationSyntax fieldDeclaration, string system, ISyntax binder, ISyntax unbinder)
        {
            var fieldType = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && x.ToString() != "Required").ToString();
            var fieldName = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is VariableDeclaratorSyntax).ToString();

            binder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = {CurrentSystem}.Owner.GetOrAddComponent<{fieldType}>();"));
            unbinder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = null;"));
        }

        /// <summary>
        /// тут мы получаем контейнер для конкретной системы
        /// </summary>
        private ISyntax GetSystemContainer(LinkedNode linkedNode, out ISyntax bind, out ISyntax unbind, out ISyntax systemPlace, out ISyntax fields)
        {
            var tree = new TreeSyntaxNode();
            var bindBody = new TreeSyntaxNode();
            var unbindBody = new TreeSyntaxNode();
            var currentSystemPlace = new TreeSyntaxNode();
            var currentFields = new TreeSyntaxNode();

            fields = currentFields;
            bind = bindBody;
            unbind = unbindBody;
            tree.Add(new TabSimpleSyntax(1, $"public sealed class {linkedNode.Name}{SystemBindContainer} : {ISystemSetter}"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(currentFields);
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public void BindSystem(ISystem system)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(currentSystemPlace);
            tree.Add(bindBody);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public void UnBindSystem(ISystem system)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(currentSystemPlace);
            tree.Add(unbindBody);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));

            systemPlace = currentSystemPlace;
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
            int i = 0;

            foreach (var s in Program.systemOverData.Values)
            {
                if (s.IsAbstract) continue;

                if (i > 0)
                    tree.Add(new ParagraphSyntax());

                i++;

                var system = s.Name;

                tree.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(system)}:"));
                tree.Add(new TabSimpleSyntax(5, $"return new {system}();"));
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
                var name = Program.componentsDeclarations[i].Identifier.ValueText;
                dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({name}), {i + 1} }},"));
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
            composite.Add(new SimpleSyntax($" new ComponentMaskAndIndex {{ComponentName = {CParse.Quote}{c.Identifier.ValueText}{(CParse.Quote)}, ComponentsMask = new {typeof(HECSMask).Name}"));
            composite.Add(new ParagraphSyntax());
            composite.Add(MaskPart);
            composite.Add(new CompositeSyntax(new TabSpaceSyntax(3), new SimpleSyntax("}},")));
            composite.Add(new ParagraphSyntax());

            MaskPart.Add(new LeftScopeSyntax(4));
            MaskPart.Add(maskBody);
            MaskPart.Add(new RightScopeSyntax(4));

            var maskSplitToArray = CalculateIndexesForMaskRoslyn(index, fieldCount);

            maskBody.Add(new TabSimpleSyntax(5, $"Index = {index + 1},"));
            maskBody.Add(new TabSimpleSyntax(5, $"TypeHashCode = {IndexGenerator.GenerateIndex(c.Identifier.ValueText)},"));

            return composite;
        }

        public int[] CalculateIndexesForMaskRoslyn(int index, int fieldCounts)
        {
            var t = new List<int>(new int[fieldCounts]);

            var ulongMaxBit = 63;
            var calculate = index + 1;
            var intPart = calculate / ulongMaxBit;
            var fractPart = calculate % ulongMaxBit;

            if (fractPart == 0)
            {
                fractPart = ulongMaxBit;
                intPart -= 1;
            }

            t[intPart] = fractPart;

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

            maskBody.Add(new TabSimpleSyntax(5, $"Index = {index + 1},"));
            maskBody.Add(new TabSimpleSyntax(5, $"TypeHashCode = {IndexGenerator.GenerateIndex(c.Identifier.ValueText)},"));
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

            tree.Add(new CompositeSyntax(new TabSpaceSyntax(1), new SimpleSyntax("public partial class ComponentContext"), new ParagraphSyntax()));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(properties);
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
                    new SimpleSyntax($"public {name} Get{name};"), new ParagraphSyntax()));

                var cArgument = name;
                var fixedArg = char.ToLower(cArgument[0]) + cArgument.Substring(1);

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
                    isHaveBody.Add(new SimpleSyntax($"(original.Mask0{i + 1} & other.Mask0{i + 1}) == other.Mask0{i + 1}"));
                else
                    isHaveBody.Add(new CompositeSyntax(new ParagraphSyntax(), new TabSpaceSyntax(6),
                        new SimpleSyntax("&&"), new SimpleSyntax($"(original.Mask0{i + 1} & other.Mask0{i + 1}) == other.Mask0{i + 1}")));

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
            tree.Add(new TabSimpleSyntax(4, "int hash = mask.Index;"));
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

            foreach (var c in Program.componentOverData.Values)
            {
                if (c.IsAbstract)
                    continue;

                var needContinue = false;

                if (c.IsPartial)
                {
                    var attr2 = c.Parts.SelectMany(x => x.AttributeLists);

                    if (attr2 != null)
                    {
                        foreach (var attributeList in attr2)
                        {
                            foreach (var a in attributeList.Attributes)
                            {
                                if (a.Name.ToString() == "HECSDefaultResolver")
                                {
                                    containersSolve.Add(c.ClassDeclaration);
                                    needContinue = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    var attributeList = c.ClassDeclaration.AttributeLists;

                    foreach (var a in attributeList)
                    {
                        foreach (var attr in a.Attributes)
                        {
                            if (attr.Name.ToString() == "HECSDefaultResolver")
                            {
                                containersSolve.Add(c.ClassDeclaration);
                                needContinue = true;
                                break;
                            }
                        }
                    }
                }

                if (needContinue)
                    continue;

                containersSolve.Add(c.ClassDeclaration);
                needResolver.Add(c.ClassDeclaration);
            }

            foreach (var c in needResolver)
            {
                list.Add((c.Identifier.ValueText + Resolver + Cs, GetResolver(Program.componentOverData[c.Identifier.ValueText]).ToString()));
            }

            return list;
        }

        private ISyntax GetResolver(LinkedNode c)
        {
            var extendedNode = new LinkedNodeExtended(c);

            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();
            var fields = new TreeSyntaxNode();
            var constructor = new TreeSyntaxNode();
            var defaultConstructor = new TreeSyntaxNode();
            var outFunc = new TreeSyntaxNode();
            var out2EntityFunc = new TreeSyntaxNode();

            var name = c.Name;

            tree.Add(usings);
            usings.Add(new UsingSyntax("Components"));
            usings.Add(new UsingSyntax("System"));
            usings.Add(new UsingSyntax("MessagePack"));
            usings.Add(new UsingSyntax("HECSFramework.Serialize"));
            usings.Add(new UsingSyntax("Commands"));

            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "[MessagePackObject, Serializable]"));
            tree.Add(new TabSimpleSyntax(1, $"public partial struct {name + Resolver} : IResolver<{name}>, IResolver<{name + Resolver},{name}>, IData"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(fields);
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public {name + Resolver} In(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(constructor);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(2, $"public void Out(ref {typeof(Entity).Name} entity)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(GetOutToEntityVoidBodyRoslyn(c.ClassDeclaration));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new TabSimpleSyntax(2, $"public void Out(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(outFunc);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            if (c.Interfaces.Any(x => x.Name == "IBeforeSerializationComponent"))
                constructor.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.BeforeSync();"));

            var typeFields = new List<GatheredField>(128);
            List<(string type, string name)> fieldsForConstructor = new List<(string type, string name)>();

            if (extendedNode.IsPrivateFieldIncluded)
            {
                usings.AddUnique(new UsingSyntax("HECSFramework.Core"));

                tree.AddUnique(new ParagraphSyntax());
                tree.Add(GetPartialClassForSerializePrivateFields(extendedNode.ClassDeclaration,
                    name + Resolver, out var saveBody, out var loadBody));

                foreach (var m in extendedNode.MemberDeclarationSyntaxes)
                {
                    if (m.IsSerializable && m.GatheredField.IsPrivate)
                    {
                        fields.Add(new TabSimpleSyntax(2, $"[Key({m.GatheredField.Order})]"));

                        if (!string.IsNullOrEmpty(m.GatheredField.ResolverName))
                        {
                            fields.Add(new TabSimpleSyntax(2, $"public {m.GatheredField.ResolverName} {m.GatheredField.FieldName};"));
                            saveBody.AddUnique(new TabSimpleSyntax(3, $"{Resolver.ToLower()}.{m.GatheredField.FieldName} = new {m.GatheredField.ResolverName}().In(ref {m.GatheredField.FieldName});"));
                            loadBody.AddUnique(new TabSimpleSyntax(3, $"{Resolver.ToLower()}.{m.GatheredField.FieldName}.Out(ref {m.GatheredField.FieldName});"));
                        }
                        else
                        {
                            fields.Add(new TabSimpleSyntax(2, $"public {m.GatheredField.Type} {m.GatheredField.FieldName};"));
                            saveBody.AddUnique(new TabSimpleSyntax(3, $"{Resolver.ToLower()}.{m.GatheredField.FieldName} = {m.GatheredField.FieldName};"));
                            loadBody.AddUnique(new TabSimpleSyntax(3, $"{m.GatheredField.FieldName} = {Resolver.ToLower()}.{m.GatheredField.FieldName};"));
                        }

                        GetNamespace(m.MemberDeclarationSyntax, usings);
                    }
                }

                constructor.Add(new TabSimpleSyntax(3, $"{extendedNode.Name.ToLower()}.Save(ref this);"));
                outFunc.Add(new TabSimpleSyntax(3, $"{extendedNode.Name.ToLower()}.Load(ref this);"));
            }

            foreach (var m in extendedNode.MemberDeclarationSyntaxes)
            {
                if (!m.GatheredField.IsSerializable)
                    continue;

                if (m.GatheredField.IsPrivate)
                    continue;

                typeFields.Add(m.GatheredField);

                GetNamespace(m.MemberDeclarationSyntax, usings);
            }

            typeFields = typeFields.Distinct().ToList();

            foreach (var f in typeFields)
            {

                fields.Add(new TabSimpleSyntax(2, $"[Key({f.Order})]"));

                if (string.IsNullOrEmpty(f.ResolverName))
                    fields.Add(new TabSimpleSyntax(2, $"public {f.Type} {f.FieldName};"));
                else
                    fields.Add(new TabSimpleSyntax(2, $"public {f.ResolverName} {f.FieldName};"));

                fieldsForConstructor.Add((f.Type, f.FieldName));

                if (f.Node is PropertyDeclarationSyntax declarationSyntax && declarationSyntax.Type.ToString().Contains("ReactiveValue"))
                {
                    constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = {c.Name.ToLower()}.{f.FieldName}.CurrentValue;"));
                    outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.{f.FieldName}.CurrentValue = this.{f.FieldName};"));
                }
                else
                {
                    if (string.IsNullOrEmpty(f.ResolverName))
                    {
                        constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = {c.Name.ToLower()}.{f.FieldName};"));
                        outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.{f.FieldName} = this.{f.FieldName};"));
                    }
                    else
                    {
                        AddUniqueSyntax(usings, new UsingSyntax("HECSFramework.Serialize"));
                        constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = new {f.ResolverName}().In(ref {c.Name.ToLower()}.{f.FieldName});"));
                        outFunc.Add(new TabSimpleSyntax(3, $"this.{f.FieldName}.Out(ref {c.Name.ToLower()}.{f.FieldName});"));
                    }
                }
            }

            if (c.Interfaces.Any(x => x.Name == "IAfterSerializationComponent"))
            {
                outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.AfterSync();"));
            }

            ////defaultConstructor.Add(DefaultConstructor(c, fieldsForConstructor, fields, constructor));
            constructor.Add(new TabSimpleSyntax(3, "return this;"));

            usings.Add(new ParagraphSyntax());
            return tree;
        }

        public (bool valid, int Order, string resolver) IsValidField(MemberDeclarationSyntax fieldDeclarationSyntax)
        {
            if (fieldDeclarationSyntax is PropertyDeclarationSyntax property)
            {
                if (property.AccessorList == null)
                    return (false, -1, string.Empty);

                var t = property.AccessorList.Accessors.FirstOrDefault(x => x.Keyword.Text == "set");

                if (t == null || t.Modifiers.Any(x => x.IsKind(SyntaxKind.PrivateKeyword) || x.IsKind(SyntaxKind.ProtectedKeyword)))
                    return (false, -1, string.Empty);
            }

            foreach (var a in fieldDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToArray())
            {
                //todo "разобраться аккуратно с аттрибутами поля"
                if (a.Name.ToString() == ("Field") && fieldDeclarationSyntax.Modifiers.ToString().Contains("public"))
                {
                    if (a.ArgumentList == null)
                        continue;
                    var resolver = string.Empty;

                    var arguments = a.ArgumentList.Arguments.ToArray();
                    var intValue = int.Parse(arguments[0].ToString());

                    if (arguments.Length > 1)
                    {
                        var data = arguments[1].ToString();
                        data = data.Replace("typeof(", "");
                        data = data.Replace(")", "");
                        resolver = data;
                    }

                    return (true, intValue, resolver);
                }
            }

            return (false, -1, string.Empty);
        }

        public static void GetNamespace(MemberDeclarationSyntax declaration, ISyntax tree)
        {
            if (declaration is FieldDeclarationSyntax field)
            {
                if (field.Declaration.Type is GenericNameSyntax generic)
                {
                    if (GetNameSpaceForCollection(generic.Identifier.Value.ToString(), out var namespaceCollection))
                    {
                        tree.AddUnique(new UsingSyntax(namespaceCollection));
                    }

                    foreach (var a in generic.TypeArgumentList.Arguments)
                    {
                        var arg = a.ToString();

                        if (Program.structByName.TryGetValue(arg, out var value))
                        {
                            if (value.Parent != null && value.Parent is NamespaceDeclarationSyntax ns)
                            {
                                tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                            }
                        }

                        if (Program.classesByName.TryGetValue(arg, out var classObject))
                        {
                            if (classObject.Parent != null && classObject.Parent is NamespaceDeclarationSyntax ns)
                            {
                                tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                            }
                        }
                    }
                }
                else
                {

                    var arg = field.Declaration.Type.ToString();

                    if (Program.structByName.TryGetValue(arg, out var value))
                    {
                        if (value.Parent != null && value.Parent is NamespaceDeclarationSyntax ns)
                        {
                            tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                        }
                    }

                    if (Program.classesByName.TryGetValue(arg, out var classObject))
                    {
                        if (classObject.Parent != null && classObject.Parent is NamespaceDeclarationSyntax ns)
                        {
                            tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                        }
                    }
                }
            }

            if (declaration is PropertyDeclarationSyntax property)
            {


                if (property.Type is GenericNameSyntax generic)
                {
                    foreach (var a in generic.TypeArgumentList.Arguments)
                    {
                        var arg = a.ToString();

                        if (Program.structByName.TryGetValue(arg, out var value))
                        {
                            if (value.Parent != null && value.Parent is NamespaceDeclarationSyntax ns)
                            {
                                tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                            }
                        }

                        if (Program.classesByName.TryGetValue(arg, out var classObject))
                        {
                            if (classObject.Parent != null && classObject.Parent is NamespaceDeclarationSyntax ns)
                            {
                                tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                            }
                        }
                    }
                }
                else
                {

                    var arg = property.Type.ToString();

                    if (Program.structByName.TryGetValue(arg, out var value))
                    {
                        if (value.Parent != null && value.Parent is NamespaceDeclarationSyntax ns)
                        {
                            tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                        }
                    }

                    if (Program.classesByName.TryGetValue(arg, out var classObject))
                    {
                        if (classObject.Parent != null && classObject.Parent is NamespaceDeclarationSyntax ns)
                        {
                            tree.AddUnique(new UsingSyntax(ns.Name.ToString()));
                        }
                    }
                }
            }
        }

        public ISyntax GetPartialClassForSerializePrivateFields(ClassDeclarationSyntax classDeclarationSyntax, string resolver, out ISyntax saveBody, out ISyntax loadBody)
        {
            var classSyntax = new TreeSyntaxNode();

            classSyntax.Add(new NameSpaceSyntax("Components"));
            classSyntax.Add(new LeftScopeSyntax());

            classSyntax.Add(new TabSimpleSyntax(1,
                $"public partial class {classDeclarationSyntax.Identifier.ValueText} : " +
                $"ISaveToResolver<{resolver}>, ILoadFromResolver<{resolver}>"));

            classSyntax.Add(new LeftScopeSyntax(1));
            classSyntax.Add(GetSaveResolverBody(resolver, out saveBody));
            classSyntax.Add(new ParagraphSyntax());
            classSyntax.Add(GetLoadResolverBody(resolver, out loadBody));
            classSyntax.Add(new RightScopeSyntax(1));
            classSyntax.Add(new RightScopeSyntax());
            return classSyntax;
        }

        public ISyntax GetSaveResolverBody(string resolver, out ISyntax body)
        {
            var tree = new TreeSyntaxNode();
            body = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, $"public void Save(ref {resolver} resolver)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }

        public ISyntax GetLoadResolverBody(string resolver, out ISyntax body)
        {
            var tree = new TreeSyntaxNode();
            body = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, $"public void Load(ref {resolver} resolver)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }

        public (bool isValid, string nameSpace) GetNameSpaceForCollection(PropertyDeclarationSyntax propertyDeclaration)
        {
            var result = (false, string.Empty);

            var kind = propertyDeclaration.Type.Kind().ToString();

            if (kind.Contains("Array") || kind.Contains("Dictionary") || kind.Contains("List"))
            {
                var collection = propertyDeclaration.Type.DescendantNodes().ToList();

                foreach (var s in collection)
                {
                    if (s is IdentifierNameSyntax nameSyntax)
                    {
                        foreach (var cl in Program.classes)
                        {
                            if (cl.Identifier.ValueText.Contains(s.ToString()))
                            {
                                var nameSpace = cl.SyntaxTree.GetRoot().ChildNodes().FirstOrDefault(x => x is NamespaceDeclarationSyntax);

                                if (nameSpace != null)
                                {
                                    foreach (var child in nameSpace.ChildNodes())
                                    {
                                        if (child is QualifiedNameSyntax nameSyntaxNamespace)
                                        {
                                            var checkedName = nameSyntaxNamespace.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            return (true, checkedName);
                                        }

                                        if (child is IdentifierNameSyntax identifierName)
                                        {
                                            var checkedName = identifierName.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            return (true, checkedName);
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var st in Program.structs)
                        {
                            if (st.Identifier.ValueText.Contains(s.ToString()))
                            {
                                var nameSpace = st.SyntaxTree.GetRoot().ChildNodes().FirstOrDefault(x => x is NamespaceDeclarationSyntax);

                                if (nameSpace != null)
                                {
                                    foreach (var child in nameSpace.ChildNodes())
                                    {
                                        if (child is QualifiedNameSyntax nameSyntaxNamespace)
                                        {
                                            var checkedName = nameSyntaxNamespace.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            return (true, checkedName);
                                        }

                                        if (child is IdentifierNameSyntax identifierName)
                                        {
                                            var checkedName = identifierName.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            return (true, checkedName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static bool GetNameSpaceForCollection(string name, out string collectionNamespace)
        {
            if (name == "Array" || name == "Dictionary" || name == "List" || name == "Dictionary" || name == "HashSet")
            {
                collectionNamespace = "System.Collections.Generic";
                return true;
            }

            collectionNamespace = string.Empty;
            return false;
        }

        public (bool isValid, ISyntax nameSpace) GetNameSpaceForCollection(FieldDeclarationSyntax field)
        {
            var kind = field.Declaration.Type.ToString();

            if (kind.Contains("Array") || kind.Contains("Dictionary") || kind.Contains("List") || kind.Contains("MoveCommandInfo"))
            {
                var collection = field.Declaration.Type.DescendantNodes().ToList();
                var currentUsings = new TreeSyntaxNode();

                if (kind.Contains("Dictionary"))
                {
                    AddUniqueSyntax(currentUsings, new UsingSyntax("System.Collections.Generic"));
                }

                foreach (var s in collection)
                {
                    if (s is TypeArgumentListSyntax arguments)
                    {
                        foreach (var a in arguments.Arguments)
                            currentUsings.Add(GetNamespaces(a.ToString()));
                    }

                    if (s is IdentifierNameSyntax nameSyntax)
                    {
                        foreach (var cl in Program.classes)
                        {
                            if (cl.Identifier.ValueText.Contains(s.ToString()))
                            {
                                var nameSpace = cl.SyntaxTree.GetRoot().ChildNodes().FirstOrDefault(x => x is NamespaceDeclarationSyntax);

                                if (nameSpace != null)
                                {
                                    foreach (var child in nameSpace.ChildNodes())
                                    {
                                        if (child is QualifiedNameSyntax nameSyntaxNamespace)
                                        {
                                            var checkedName = nameSyntaxNamespace.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            currentUsings.Add(new UsingSyntax(checkedName));
                                        }

                                        if (child is IdentifierNameSyntax identifierName)
                                        {
                                            var checkedName = identifierName.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            currentUsings.Add(new UsingSyntax(checkedName));
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var st in Program.structs)
                        {
                            if (st.Identifier.ValueText.Contains(s.ToString()))
                            {
                                var nameSpace = st.SyntaxTree.GetRoot().ChildNodes().FirstOrDefault(x => x is NamespaceDeclarationSyntax);

                                if (nameSpace != null)
                                {
                                    foreach (var child in nameSpace.ChildNodes())
                                    {
                                        if (child is QualifiedNameSyntax nameSyntaxNamespace)
                                        {
                                            var checkedName = nameSyntaxNamespace.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            currentUsings.Add(new UsingSyntax(checkedName));
                                        }

                                        if (child is IdentifierNameSyntax identifierName)
                                        {
                                            var checkedName = identifierName.ToString();
                                            if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                                                continue;

                                            currentUsings.Add(new UsingSyntax(checkedName));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return (true, currentUsings);
            }

            return (false, null);
        }

        private ISyntax GetNamespaces(string nameOfNode, bool isInterface = false)
        {
            var tree = new TreeSyntaxNode();

            var classes = Program.classes.Where(x => x.Identifier.ValueText == nameOfNode).ToList();
            var structs = Program.structs.Where(x => x.Identifier.ValueText == nameOfNode).ToList();
            var interfaces = Program.interfaces.Where(x => x.Identifier.ValueText == nameOfNode).ToList();

            var need = new List<TypeDeclarationSyntax>();
            need.AddRange(classes);
            need.AddRange(structs);
            //need.AddRange(interfaces);

            foreach (var i in interfaces)
            {
                if (i.Parent is NamespaceDeclarationSyntax nspace)
                {
                    if (nspace.Name is IdentifierNameSyntax identifier)
                    {
                        AddUniqueSyntax(tree, new UsingSyntax(identifier.ToString()));
                    }
                    else if (nspace.Name is QualifiedNameSyntax identifier2)
                    {
                        AddUniqueSyntax(tree, new UsingSyntax(identifier2.ToString()));
                    }
                }
            }

            foreach (var c in need)
            {
                var childNodes = c.ChildNodes();

                foreach (var child in childNodes)
                {
                    if (child is QualifiedNameSyntax nameSyntaxNamespace)
                    {
                        var checkedName = nameSyntaxNamespace.ToString();
                        if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                            continue;

                        tree.Add(new UsingSyntax(checkedName));
                    }

                    if (child is IdentifierNameSyntax identifierName)
                    {
                        var checkedName = identifierName.ToString();
                        if (checkedName == string.Empty || checkedName == "MessagePack.Resolvers")
                            continue;

                        tree.Add(new UsingSyntax(checkedName));
                    }
                }
            }

            return tree;
        }


        private string GetNameSpace(PropertyDeclarationSyntax field)
        {
            var neededClass = Program.classes.FirstOrDefault(x => x.Identifier.ValueText == field.Identifier.ToString());
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
                var needed = property.AccessorList.Accessors.FirstOrDefault(x => x.Kind() == SyntaxKind.SetAccessorDeclaration);

                if (needed == null)
                    return (false, -1);

                foreach (var a in property.AttributeLists.SelectMany(x => x.Attributes).ToArray())
                {
                    if (a.ToString().Contains("Field") && property.Modifiers.ToString().Contains("public"))
                    {
                        var intValue = int.Parse(a.ArgumentList.Arguments.ToArray()[0].ToString());
                        Console.WriteLine("нашли реактив проперти");
                        return (true, intValue);
                    }
                }
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
                    var intValue = int.Parse(a.ArgumentList?.Arguments.ToArray()[0].ToString() ?? "0");
                    return (true, intValue);
                }
            }

            return (false, -1);
        }

        private ISyntax GetOutToEntityVoidBodyRoslyn(ClassDeclarationSyntax c)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(3, $"var local = entity.GetComponent<{c.Identifier.ValueText}>();"));
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
            tree.Add(new UsingSyntax("MessagePack.Resolvers"));
            tree.Add(new UsingSyntax("MessagePack", 1));
            tree.Add(GetUnionResolvers());
            tree.Add(new ParagraphSyntax());
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            //tree.Add(GetResolverMapStaticConstructor()); we move this to client when mpc codogen
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

        private ISyntax GetResolverMapStaticConstructor()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "private static bool isMessagePackInited;"));
            tree.Add(new TabSimpleSyntax(3, "static ResolversMap()"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, "if (isMessagePackInited)"));
            tree.Add(new TabSimpleSyntax(5, "return;"));
            tree.Add(new TabSimpleSyntax(4, "StaticCompositeResolver.Instance.Register(StandardResolver.Instance, GeneratedResolver.Instance);"));
            tree.Add(new TabSimpleSyntax(4, "isMessagePackInited = true;"));
            tree.Add(new TabSimpleSyntax(4, "MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard.WithResolver(StaticCompositeResolver.Instance);"));
            tree.Add(new RightScopeSyntax());
            tree.Add(new ParagraphSyntax());

            return tree;
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
            tree.Add(new TabSimpleSyntax(2, "private void ProcessResolverContainerRealisation(ref ResolverDataContainer dataContainerForResolving, ref Entity entity)"));
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
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}{Resolver.ToLower()} = MessagePackSerializer.Deserialize<{name}{Resolver}>(dataContainerForResolving.Data);"));
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}component = entity.GetOrAddComponent<{name}>();"));
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
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}data = MessagePackSerializer.Deserialize<{name}{Resolver}>(resolverDataContainer.Data);"));
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
                caseBody.Add(new TabSimpleSyntax(5, $"var {name}{Resolver.ToLower()} = MessagePackSerializer.Deserialize<{name}{Resolver}>(dataContainerForResolving.Data);"));
                caseBody.Add(new TabSimpleSyntax(5, $"if (EntityManager.TryGetEntityByID(dataContainerForResolving.EntityGuid, out var entityOf{name}))"));
                caseBody.Add(new LeftScopeSyntax(5));
                caseBody.Add(new TabSimpleSyntax(6, $"var {name}component = entityOf{name}.GetOrAddComponent<{name}>();"));
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
            tree.Add(new TabSimpleSyntax(3, "InitPartialCommandResolvers();"));
            tree.Add(new TabSimpleSyntax(3, "InitCustomResolvers();"));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(2, "partial void InitCustomResolvers();"));
            return tree;
        }
        #endregion

        #region CustomAndUniversalResolvers

        public string GetCustomResolversMap()
        {
            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();
            tree.Add(usings);

            usings.AddUnique(new UsingSyntax("System"));
            usings.AddUnique(new UsingSyntax("System.Collections.Generic"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(GetUniversalResolvers(usings));
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(GetCustomProvidersPartialInitMethod());
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetTypeToCustomResolver", "Type", "CustomResolverProviderBase", 2, out var customTypeToResolvers));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetTypeCodeToCustomResolver", "int", "CustomResolverProviderBase", 2, out var typeCodeToCustomResolver));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetTypeIndexToType", "int", "Type", 2, out var typeIndexToType));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            foreach (var cr in Program.customHecsResolvers)
            {
                if (Program.classesByName.TryGetValue(cr.Key, out var classNeeded))
                {
                    if (classNeeded.Parent is NamespaceDeclarationSyntax namespaceDeclaration)
                    {
                        usings.AddUnique(new UsingSyntax(namespaceDeclaration.Name.ToString()));
                    }
                }

                customTypeToResolvers.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, $"typeof({cr.Key})",
                    $"new CustomResolverProvider<{cr.Key}, {cr.Value.ResolverName}>()"));

                typeCodeToCustomResolver.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, $"{IndexGenerator.GenerateIndex(cr.Key)}",
                    $"new CustomResolverProvider<{cr.Key}, {cr.Value.ResolverName}>()"));

                typeIndexToType.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, $"{IndexGenerator.GenerateIndex(cr.Key)}",
                    $"typeof({cr.Key})"));
            }

            usings.Add(new ParagraphSyntax());
            return tree.ToString();
        }

        private ISyntax GetUniversalResolvers(ISyntax usings)
        {
            var tree = new TreeSyntaxNode();
            foreach (var ur in Program.hecsResolverCollection)
            {
                tree.Add(GetUniversalResolver(ur.Value, usings));
            }

            return tree;
        }

        private ISyntax GetUniversalResolver(LinkedNode c, ISyntax usings)
        {
            c.GetAllParentsAndParts(c.Parts);

            var tree = new TreeSyntaxNode();
            var fields = new TreeSyntaxNode();
            var constructor = new TreeSyntaxNode();
            var defaultConstructor = new TreeSyntaxNode();
            var outFunc = new TreeSyntaxNode();
            var out2EntityFunc = new TreeSyntaxNode();

            var name = c.Name;

            usings.AddUnique(new UsingSyntax("System"));
            usings.AddUnique(new UsingSyntax("Commands"));
            usings.AddUnique(new UsingSyntax("Components"));
            usings.AddUnique(new UsingSyntax("MessagePack"));
            usings.AddUnique(new UsingSyntax("HECSFramework.Serialize"));

            tree.Add(new TabSimpleSyntax(1, "[MessagePackObject, Serializable]"));
            tree.Add(new TabSimpleSyntax(1, $"public partial struct {name + Resolver} : IResolver<{name + Resolver},{name}>, IData"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(fields);
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"public {name + Resolver} In(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(constructor);
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new TabSimpleSyntax(2, $"public void Out(ref {name} {name.ToLower()})"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(outFunc);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new ParagraphSyntax());


            c.Interfaces.Clear();
            c.GetInterfaces(c.Interfaces);

            if (c.Interfaces.Any(x => x.Name == "IBeforeSerializationComponent"))
                constructor.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.BeforeSync();"));

            //((c.Members.ToArray()[0] as FieldDeclarationSyntax).AttributeLists.ToArray()[0].Attributes.ToArray()[0] as AttributeSyntax).ArgumentList.Arguments.ToArray()[0].ToString()
            var typeFields = new List<GatheredField>(128);
            List<(string type, string name)> fieldsForConstructor = new List<(string type, string name)>();

            foreach (var parts in c.Parts)
            {
                foreach (var m in parts.Members)
                {
                    if (m is MemberDeclarationSyntax member)
                    {
                        var validate = IsValidField(member);

                        if (!validate.valid) continue;


                        GetNamespace(member, usings);

                        string type = "";
                        string fieldName = "";

                        if (member is FieldDeclarationSyntax field)
                        {
                            fieldName = field.Declaration.Variables[0].Identifier.ToString();
                            type = field.Declaration.Type.ToString();
                        }

                        if (member is PropertyDeclarationSyntax property)
                        {
                            fieldName = property.Identifier.Text;
                            type = property.Type.ToString();
                        }

                        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(fieldName))
                            throw new Exception("we dont have type for field " + m.ToString());

                        if (validate.valid)
                        {
                            if (typeFields.Any(x => x.Order == validate.Order || x.FieldName == fieldName))
                                continue;

                            typeFields.Add(new GatheredField
                            {
                                Order = validate.Order,
                                Type = type,
                                FieldName = fieldName,
                                ResolverName = validate.resolver,
                                Node = member
                            });
                        }
                    }
                }
            }

            typeFields = typeFields.Distinct().ToList();

            foreach (var f in typeFields)
            {

                fields.Add(new TabSimpleSyntax(2, $"[Key({f.Order})]"));

                if (string.IsNullOrEmpty(f.ResolverName))
                    fields.Add(new TabSimpleSyntax(2, $"public {f.Type} {f.FieldName};"));
                else
                    fields.Add(new TabSimpleSyntax(2, $"public {f.ResolverName} {f.FieldName};"));

                fieldsForConstructor.Add((f.Type, f.FieldName));

                if (f.Node is PropertyDeclarationSyntax declarationSyntax && declarationSyntax.Type.ToString().Contains("ReactiveValue"))
                {
                    constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = {c.Name.ToLower()}.{f.FieldName}.CurrentValue;"));
                    outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.{f.FieldName}.CurrentValue = this.{f.FieldName};"));
                }
                else
                {
                    if (string.IsNullOrEmpty(f.ResolverName))
                    {
                        constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = {c.Name.ToLower()}.{f.FieldName};"));
                        outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.{f.FieldName} = this.{f.FieldName};"));
                    }
                    else
                    {
                        AddUniqueSyntax(usings, new UsingSyntax("HECSFramework.Serialize"));
                        constructor.Add(new TabSimpleSyntax(3, $"this.{f.FieldName} = new {f.ResolverName}().In(ref {c.Name.ToLower()}.{f.FieldName});"));
                        outFunc.Add(new TabSimpleSyntax(3, $"this.{f.FieldName}.Out(ref {c.Name.ToLower()}.{f.FieldName});"));
                    }
                }
            }

            if (c.Interfaces.Any(x => x.Name == "IAfterSerializationComponent"))
            {
                outFunc.Add(new TabSimpleSyntax(3, $"{c.Name.ToLower()}.AfterSync();"));
            }

            ////defaultConstructor.Add(DefaultConstructor(c, fieldsForConstructor, fields, constructor));
            constructor.Add(new TabSimpleSyntax(3, "return this;"));

            usings.Tree.Add(new ParagraphSyntax());
            return tree;
        }

        private ISyntax GetCustomProvidersPartialInitMethod()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "partial void InitCustomResolvers()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "typeToCustomResolver = GetTypeToCustomResolver();"));
            tree.Add(new TabSimpleSyntax(3, "typeCodeToCustomResolver = GetTypeCodeToCustomResolver();"));
            tree.Add(new TabSimpleSyntax(3, "getTypeIndexToType = GetTypeIndexToType();"));
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

            foreach (var s in Program.systemOverData)
            {
                if (s.Value.IsAbstract) continue;

                var name = s.Key;
                dictionaryBody.Add(new TabSimpleSyntax(3, $" {CParse.LeftScope} typeof({name}), typeof({name}{BluePrint}) {CParse.RightScope},"));
            }

            return tree;
        }
        #endregion

        #region GenerateSystemsBluePrints
        public List<(string name, string classBody)> GenerateSystemsBluePrints()
        {
            var list = new List<(string name, string classBody)>();

            foreach (var c in Program.systemOverData.Values)
            {
                if (c.IsAbstract) continue;

                var name = c.Name;
                list.Add((name + BluePrint + ".cs", GetSystemBluePrint(c.ClassDeclaration)));
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

        #region PredicatesBluePrints

        public List<(string, string)> GetPredicateBluePrints()
        {
            var newList = new List<(string, string)>(2048);

            var count = Program.classes.Count;
            var classes = Program.classes;

            for (int i = 0; i < count; i++)
            {
                var currentClass = classes[i];

                if (currentClass.Modifiers.Any(x => x.ValueText == "abstract"))
                    continue;

                if (currentClass.BaseList != null && currentClass.BaseList.Types.Any(x => x.ToString()==("IPredicate")))
                {
                    newList.Add(($"{currentClass.Identifier.ValueText}Blueprint.cs", GetPredicateBluePrintSyntax(currentClass).ToString()));
                }
            }

            return newList;
        }

        private ISyntax GetPredicateBluePrintSyntax(ClassDeclarationSyntax classDeclarationSyntax)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("HECSFramework.Core"));
            tree.Add(new UsingSyntax("HECSFramework.Unity"));
            tree.Add(new UsingSyntax("Predicates"));
            tree.Add(new UsingSyntax("UnityEngine", 1));

            tree.Add(new TabSimpleSyntax(0, $"[CreateAssetMenu(fileName = {CParse.Quote}{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote}, menuName = {CParse.Quote}BluePrints/Predicates/{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote})]"));
            tree.Add(new TabSimpleSyntax(0, $"public class {classDeclarationSyntax.Identifier.ValueText}Blueprint : PredicateBluePrintContainer<{classDeclarationSyntax.Identifier}>"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new RightScopeSyntax());
            return tree;
        }

        #endregion

        #region ActionPredicates
        public List<(string, string)> GetActionsBluePrints()
        {
            var newList = new List<(string, string)>(2048);

            var count = Program.classes.Count;
            var classes = Program.classes;

            for (int i = 0; i < count; i++)
            {
                var currentClass = classes[i];

                if (currentClass.BaseList != null && currentClass.BaseList.Types.Any(x => x.ToString() == ("IAction")))
                {
                    newList.Add(($"{currentClass.Identifier.ValueText}Blueprint.cs", GetActionsBluePrintSyntax(currentClass).ToString()));
                }
            }

            return newList;
        }

        private ISyntax GetActionsBluePrintSyntax(ClassDeclarationSyntax classDeclarationSyntax)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("HECSFramework.Core"));
            tree.Add(new UsingSyntax("HECSFramework.Unity"));
            tree.Add(new UsingSyntax("UnityEngine", 1));

            tree.Add(new TabSimpleSyntax(0, $"[CreateAssetMenu(fileName = {CParse.Quote}{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote}, menuName = {CParse.Quote}BluePrints/Actions/{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote})]"));
            tree.Add(new TabSimpleSyntax(0, $"public class {classDeclarationSyntax.Identifier.ValueText}Blueprint : ActionBluePrint<{classDeclarationSyntax.Identifier}>"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new RightScopeSyntax());
            return tree;
        }
        #endregion

        #region CommandsResolvers

        /// <summary>
        /// we generate here commands map and short ids staff
        /// </summary>
        /// <param name="commands"></param>
        /// <returns></returns>
        public string GenerateNetworkCommandsAndShortIdsMap(List<StructDeclarationSyntax> commands)
        {
            var tree = new TreeSyntaxNode();
            var resolvers = new TreeSyntaxNode();
            var typeToIdDictionary = new TreeSyntaxNode();
            var dictionaryBody = new TreeSyntaxNode();
            var genericMethod = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("Commands"));
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("HECSFramework.Serialize"));
            tree.Add(new UsingSyntax("System.Collections.Generic", 1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, "public Dictionary<int, ICommandResolver> Map = new Dictionary<int, ICommandResolver>"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(resolvers);
            tree.Add(new RightScopeSyntax(2, true));
            tree.Add(new ParagraphSyntax());
            tree.Add(typeToIdDictionary);
            tree.Add(new ParagraphSyntax());
            tree.Add(GetShortIdPart());
            tree.Add(new ParagraphSyntax());
            tree.Add(InitPartialCommandResolvers());
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax(0));

            foreach (var t in commands)
                resolvers.Add(GetCommandResolver(t));

            typeToIdDictionary.Add(new TabSimpleSyntax(2, "public Dictionary<Type, int> CommandsIDs = new Dictionary<Type, int>"));
            typeToIdDictionary.Add(new LeftScopeSyntax(2));
            typeToIdDictionary.Add(dictionaryBody);
            typeToIdDictionary.Add(new RightScopeSyntax(2, true));

            for (int i = 0; i < commands.Count; i++)
            {
                var t = commands[i];
                dictionaryBody.Add(GetCommandMethod(t));

                //if (i < commands.Count - 1)
                //    dictionaryBody.Add(new ParagraphSyntax());
            }

            return tree.ToString();
        }


        /// <summary>
        /// here we codogen all around shortIDs
        /// </summary>
        /// <returns></returns>
        public ISyntax GetShortIdPart()
        {
            var tree = new TreeSyntaxNode();
            HashSet<ShortIDObject> shortIDs = new HashSet<ShortIDObject>(512);
            ushort count = 1;

            //gather network components
            foreach (var c in Program.componentOverData.Values)
            {
                if (c.IsAbstract)
                    continue;

                foreach (var i in c.Interfaces)
                {
                    if (i.Name == INetworkComponent)
                    {
                        shortIDs.Add(new ShortIDObject
                        {
                            Type = c.Name,
                            TypeCode = IndexGenerator.GenerateIndex(c.Name),
                            DataType = 2,
                        });
                    }
                }
            }

            foreach (var c in Program.networkCommands)
            {
                var shortIDdata = new ShortIDObject();

                shortIDdata.Type = c.Identifier.ValueText;
                shortIDdata.TypeCode = IndexGenerator.GenerateIndex(c.Identifier.ValueText);

                if (c.BaseList.ChildNodes().Any(x => x.ToString().Contains("INetworkCommand")))
                {
                    shortIDdata.DataType = 0;
                }
                else
                {
                    shortIDdata.DataType = 1;
                }

                shortIDs.Add(shortIDdata);
            }

            shortIDs = shortIDs.OrderBy(x => x.Type).ToHashSet();

            foreach (var i in shortIDs)
            {
                i.ShortId = count;
                count++;
            }

            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetTypeToShort", nameof(Type), "ushort", 2, out var typeToshortBody));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetShortToTypeCode", "ushort", "int", 2, out var shortToTypeCodeBody));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetShortToDataType", "ushort", "byte", 2, out var getShortToDataType));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetTypeCodeToShort", "int", "ushort", 2, out var typeCodeToShort));
            tree.Add(GetDictionaryHelper.GetDictionaryMethod("GetComponentProviders", "int", "ComponentSerializeProvider", 2, out var componentProviders));

            foreach (var i in shortIDs)
            {
                typeToshortBody.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, $"typeof({i.Type})", i.ShortId.ToString()));
                shortToTypeCodeBody.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, i.ShortId.ToString(), i.TypeCode.ToString()));
                getShortToDataType.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, i.ShortId.ToString(), i.DataType.ToString()));
                typeCodeToShort.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4, i.TypeCode.ToString(), i.ShortId.ToString()));
            }

            foreach (var c in Program.componentOverData.Values)
            {
                if (c.IsAbstract)
                    continue;

                if (c.Interfaces.Any(x => x.Name == INetworkComponent))
                {
                    componentProviders.Tree.Add(GetDictionaryHelper.DictionaryBodyRecord(4,
                        IndexGenerator.GenerateIndex(c.Name).ToString(), $"new ComponentResolver<{c.Name},{c.Name}{Resolver}, {c.Name}{Resolver}>()"));
                }
            }


            tree.Add(InitShortIDPart());

            return tree;
        }

        private ISyntax InitShortIDPart()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "private void InitShortIds()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "typeToShort = GetTypeToShort();"));
            tree.Add(new TabSimpleSyntax(3, "shortToTypeCode = GetShortToTypeCode();"));
            tree.Add(new TabSimpleSyntax(3, "shortToDataType = GetShortToDataType();"));
            tree.Add(new TabSimpleSyntax(3, "typeCodeToShort = GetTypeCodeToShort();"));
            tree.Add(new TabSimpleSyntax(3, "componentProviders = GetComponentProviders();"));
            tree.Add(new RightScopeSyntax(2));

            return tree;
        }

        private ISyntax GetCommandMethod(StructDeclarationSyntax command)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(3, $"{{typeof({command.Identifier.ValueText}), {IndexGenerator.GetIndexForType(command.Identifier.ValueText)}}},"));
            return tree;
        }

        private ISyntax InitPartialCommandResolvers()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(2, "partial void InitPartialCommandResolvers()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "hashTypeToResolver = Map;"));
            tree.Add(new TabSimpleSyntax(3, "typeTohash = CommandsIDs;"));

            ///this part of short ids, u should check GetShortIdPart()
            tree.Add(new TabSimpleSyntax(3, "InitShortIds();"));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new ParagraphSyntax());

            return tree;
        }

        private ISyntax GetCommandResolver(StructDeclarationSyntax type)
        {
            return new TabSimpleSyntax(3, $"{{{IndexGenerator.GetIndexForType(type.Identifier.ValueText)}, new CommandResolver<{type.Identifier.ValueText}>()}},");
        }

        #endregion

        #region Documentation

        public string GetDocumentationRoslyn()
        {
            var tree = new TreeSyntaxNode();

            tree.Add(new UsingSyntax("System.Collections.Generic", 1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class HECSDocumentation"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(GetDocumentationConstructorRoslyn());
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }

        private ISyntax GetDocumentationConstructorRoslyn()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(2, "public HECSDocumentation()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "Documentations = new List<DocumentationRepresentation>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(GetDocumentRepresentationArrayRoslyn());
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));
            return tree;
        }

        private ISyntax GetDocumentRepresentationArrayRoslyn()
        {
            var tree = new TreeSyntaxNode();

            var typeHolder = new Dictionary<string, (List<string> segments, List<string> comments, string Type)>(64);

            foreach (var t in Program.classes)
            {
                ProcessDocumentationAttribute(t, typeHolder);
            }

            //foreach (var collected in typeHolder)
            //{
            //    tree.Add(new TabSimpleSyntax(4, "new DocumentationRepresentation"));
            //    tree.Add(new LeftScopeSyntax(4));
            //    tree.Add(GetStringArrayRoslyn("SegmentTypes", collected.Value.segments));
            //    tree.Add(GetStringArrayRoslyn("Comments", collected.Value.comments));
            //    tree.Add(new TabSimpleSyntax(5, $"DataType = {CParse.Quote + collected.Value.Type + CParse.Quote},"));
            //    tree.Add(GetDocumentationTypeRoslyn(collected.Key));
            //    tree.Add(new RightScopeSyntax(4) { IsCommaNeeded = true });
            //}
            return tree;
        }

        public void ProcessDocumentationAttribute(TypeDeclarationSyntax type, Dictionary<string, (List<string> segments, List<string> comments, string Type)> typeHolder)
        {
            var attributes = type.ChildNodes().Where(x => x is AttributeListSyntax attribute && attribute.ToString().Contains("Documentation")).Select(z => z as AttributeListSyntax);

            if (attributes == null)
                return;

            var t = type.Identifier.Text;

            if (!typeHolder.ContainsKey(t))
                typeHolder.Add(t, (new List<string>(), new List<string>(), t));

            foreach (var a in attributes)
            {
                //foreach (var d in documentation.SegmentType)
                //    typeHolder[t].segments.Add(d);

                //typeHolder[t].comments.Add(documentation.Comment);
            }
        }

        private ISyntax GetDocumentationTypeRoslyn(Type type)
        {
            var tree = new TreeSyntaxNode();
            string documentationType;

            if (componentTypes.Contains(type))
                documentationType = "DocumentationType.Component";
            else if (systems.Contains(type))
                documentationType = "DocumentationType.System";
            else
                documentationType = "DocumentationType.Common";

            tree.Add(new TabSimpleSyntax(5, $"DocumentationType = {documentationType},"));

            return tree;
        }

        private ISyntax GetStringArrayRoslyn(string name, List<string> toArray)
        {
            var tree = new TreeSyntaxNode();
            var body = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(5, $"{name} = new string[]"));
            tree.Add(new LeftScopeSyntax(5));
            tree.Add(body);
            tree.Add(new CompositeSyntax(new TabSpaceSyntax(5), new SimpleSyntax(CParse.RightScope + CParse.Comma + CParse.Paragraph)));

            foreach (var s in toArray)
            {
                if (string.IsNullOrEmpty(s))
                    continue;

                body.Add(new TabSimpleSyntax(6, $"{CParse.Quote + s + CParse.Quote + CParse.Comma}"));
            }

            return tree;
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