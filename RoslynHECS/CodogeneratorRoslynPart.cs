using RoslynHECS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
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
            tree.Add(new TabSimpleSyntax(3, $"Count = {componentTypes.Count + 1};"));
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
            var m = ComponentsCount();

            for (int i = 0; i < componentTypesByClass.Count; i++)
            {
                Type c = componentTypesByClass[i];
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
            getComponentFunc.Add(GetSystemsByHashCode());
            getComponentFunc.Add(new RightScopeSyntax(3));
            getComponentFunc.Add(new ParagraphSyntax());
            getComponentFunc.Add(new TabSimpleSyntax(3, "return default;"));
            getComponentFunc.Add(new RightScopeSyntax(2));

            return tree;
        }

        private ISyntax GetComponentsByHashCodeRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < componentTypes.Count; i++)
            {
                if (i > 0)
                    tree.Add(new ParagraphSyntax());

                var component = componentTypes[i];

                tree.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(component)}:"));
                tree.Add(new TabSimpleSyntax(5, $"return new {component.Name}();"));
            }

            return tree;
        }

        private ISyntax GetSystemsByHashCodeRoslyn()
        {
            var tree = new TreeSyntaxNode();

            for (int i = 0; i < systems.Count; i++)
            {
                if (i > 0)
                    tree.Add(new ParagraphSyntax());

                var component = systems[i];

                tree.Add(new TabSimpleSyntax(4, $"case {IndexGenerator.GetIndexForType(component)}:"));
                tree.Add(new TabSimpleSyntax(5, $"return new {component.Name}();"));
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

            for (int i = 0; i < componentTypes.Count; i++)
            {
                var hash = IndexGenerator.GetIndexForType(componentTypes[i]);
                dicBody.Add(new TabSimpleSyntax(4, $"{{ {hash}, typeof({componentTypes[i].Name})}},"));
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

            for (int i = 0; i < componentTypes.Count; i++)
            {
                var interfaces = componentTypes[i].GetInterfaces();

                foreach (var @interface in interfaces)
                {
                    if (@interface.Name.Contains(componentTypes[i].Name))
                        dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({@interface.Name}), {i} }},"));
                }

                dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({componentTypes[i].Name}), {i} }},"));
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

            for (int i = 0; i < componentTypes.Count; i++)
            {
                var interfaces = componentTypes[i].GetInterfaces();
                var hash = IndexGenerator.GetIndexForType(componentTypes[i]);

                foreach (var @interface in interfaces)
                {
                    if (@interface.Name.Contains(componentTypes[i].Name))
                        dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({@interface.Name}), {hash} }},"));
                }

                dicBody.Add(new TabSimpleSyntax(4, $"{{ typeof({componentTypes[i].Name}), {hash} }},"));
            }

            return tree;
        }

        private ISyntax GetComponentForTypeMapRoslyn(int index, int fieldCount, Type c)
        {
            var composite = new TreeSyntaxNode();
            var MaskPart = new TreeSyntaxNode();
            var maskBody = new TreeSyntaxNode();

            composite.Add(new ParagraphSyntax());
            composite.Add(new TabSpaceSyntax(3));
            composite.Add(new SimpleSyntax(CParse.LeftScope));
            composite.Add(new CompositeSyntax(new SimpleSyntax(CParse.Space + IndexGenerator.GetIndexForType(c).ToString() + CParse.Comma)));
            composite.Add(new SimpleSyntax($" new ComponentMaskAndIndex {{ComponentName = {CParse.Quote}{ c.Name }{(CParse.Quote)}, ComponentsMask = new {typeof(HECSMask).Name}"));
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


        #region Helpers
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
