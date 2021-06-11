using HECSFramework.Core.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
