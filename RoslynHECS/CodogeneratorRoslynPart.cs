using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
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
        public const string Resolver = "Resolver";
        public const string Cs = ".cs";
        private string ResolverContainer = "ResolverDataContainer";
        public const string BluePrint = "BluePrint";


        public const string IReactGlobalCommand = "IReactGlobalCommand";
        public const string INetworkComponent = "INetworkComponent";
        public const string IRequestProvider = "IRequestProvider";
        public const string IReactCommand = "IReactCommand";
        public const string IReactComponentLocal = "IReactComponentLocal";
        public const string IReactComponentGlobal = "IReactComponentGlobal";

        public const string IReactGenericGlobalComponent = "IReactGenericGlobalComponent";
        public const string IReactGenericLocalComponent = "IReactGenericLocalComponent";

        public const string CurrentSystem = "currentSystem";

        public const string IReactNetworkCommandGlobal = "IReactNetworkCommandGlobal";
        public const string IReactNetworkCommandLocal = "IReactNetworkCommandLocal";
        public const string GenericNetworkCommand = "GenericNetworkCommand";
        public const string IRequestProcessor = "IRequestProcessor";

        private HashSet<LinkedInterfaceNode> interfaceCache = new HashSet<LinkedInterfaceNode>(64);
        private HashSet<LinkedGenericInterfaceNode> interfaceGenericCache = new HashSet<LinkedGenericInterfaceNode>(64);
        private HashSet<ClassDeclarationSyntax> systemCasheParentsAndPartial = new HashSet<ClassDeclarationSyntax>(64);

        #region SystemsBinding
        private void ProcessReacts(LinkedGenericInterfaceNode part, ISyntax bindContainerBody, ISyntax unbindContainer, LinkedNode systemNode, TreeSyntaxNode usingSpaces)
        {
            switch (part.BaseInterface.Name)
            {
                case IReactCommand:

                    //это костыль для дженерик систем с командами, возможно стоит отдельный модуль при обработке систем выделить и наполнение линкед ноды
                    if (systemNode.Parent != null && systemNode.Parent.IsGeneric && part.GenericType == "T")
                    {
                       var nodes = systemNode.ClassDeclaration.BaseList.DescendantNodes();

                        foreach (var n in nodes)
                        {
                            if (n is GenericNameSyntax generic)
                            {
                                part.GenericType = generic.TypeArgumentList.Arguments[0].ToString(); 
                            }
                        }
                    }

                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"LocalCommandListener<{part.GenericType}>.AddListener(currentSystem.Owner.World.Index,{CurrentSystem});"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"LocalCommandListener<{part.GenericType}>.RemoveListener(currentSystem.Owner.WorldId, system);"));
                    break;
                case IReactGlobalCommand:
                    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactCommand<{part.GenericType}>(system, {CurrentSystem});"));
                    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.RemoveGlobalReactCommand<{part.GenericType}>(system);"));
                    break;
                case IRequestProvider:
                {
                    if (part.MultiArguments)
                    {
                        usingSpaces.AddUnique(GetNamespaces(part.GenericNameSyntax));
                        bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddRequestProvider<{part.GenericNameSyntax.TypeArgumentList.Arguments[0]},{part.GenericNameSyntax.TypeArgumentList.Arguments[1]}>({CurrentSystem});"));
                        unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.RemoveRequestProvider<{part.GenericNameSyntax.TypeArgumentList.Arguments[0]},{part.GenericNameSyntax.TypeArgumentList.Arguments[1]}>({CurrentSystem});"));
                    }
                    else
                    {
                        usingSpaces.AddUnique(GetNamespaces(part.GenericType));
                        bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddRequestProvider<{part.GenericType}>({CurrentSystem});"));
                        unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.RemoveRequestProvider<{part.GenericType}>({CurrentSystem});"));
                    }
                    break;
                }
                //case IReactComponentLocal:
                //    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, true);"));
                //    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, false);"));
                //    break;
                //case IReactComponentGlobal:
                //    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactComponent<{part.GenericType}>({CurrentSystem}, true);"));
                //    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalReactComponent<{part.GenericType}>({CurrentSystem}, false);"));
                //    break;
                //case IReactGenericLocalComponent:
                //    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalGenericReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, true);"));
                //    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddLocalGenericReactComponent<{part.GenericType}>(system.Owner.Index, {CurrentSystem}, false);"));
                //    break;
                //case IReactGenericGlobalComponent:
                //    bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalGenericReactComponent<{part.GenericType}>({CurrentSystem}, true);"));
                //    unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"system.Owner.World.AddGlobalGenericReactComponent<{part.GenericType}>({CurrentSystem}, false);"));
                //    break;
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
                    case IRequestProcessor:
                        if (!part.MultiArguments)
                            GetRequestResponseBody(part, bindContainerBody, unbindContainer, systemNode);
                        else
                            GetRequestContextResponseBody(part, bindContainerBody, unbindContainer, systemNode);
                        break;
                }
            }
        }

        private void GetRequestResponseBody(LinkedGenericInterfaceNode part, ISyntax bindContainerBody, ISyntax unbindContainer, LinkedNode systemNode)
        {
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#if SERVER"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"Server.ServerHelpers.GetSingleSystemFromServer<DataSenderSystem>(system.Owner.World).RegisterRequestProcessor<{part.GenericType}>(currentSystem, true);"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#else"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"EntityManager.GetSingleSystem<DataSenderSystem>().RegisterRequestProcessor<{part.GenericType}>(currentSystem, true);"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#endif"));

            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#if SERVER"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"Server.ServerHelpers.GetSingleSystemFromServer<DataSenderSystem>(system.Owner.World)?.RegisterRequestProcessor<{part.GenericType}>(currentSystem, false);"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#else"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"EntityManager.GetSingleSystem<DataSenderSystem>()?.RegisterRequestProcessor<{part.GenericType}>(currentSystem, false);"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#endif"));
        }

        private void GetRequestContextResponseBody(LinkedGenericInterfaceNode part, ISyntax bindContainerBody, ISyntax unbindContainer, LinkedNode systemNode)
        {
            var secondType = part.GenericNameSyntax.TypeArgumentList.Arguments[1].ToString();

            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#if SERVER"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"Server.ServerHelpers.GetSingleSystemFromServer<DataSenderSystem>(system.Owner.World).RegisterRequestProcessor<{part.GenericType},{secondType}>(currentSystem, true);"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#else"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"EntityManager.GetSingleSystem<DataSenderSystem>().RegisterRequestProcessor<{part.GenericType},{secondType}>(currentSystem, true);"));
            bindContainerBody.Tree.Add(new TabSimpleSyntax(3, $"#endif"));

            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#if SERVER"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"Server.ServerHelpers.GetSingleSystemFromServer<DataSenderSystem>(system.Owner.World)?.RegisterRequestProcessor<{part.GenericType},{secondType}>(currentSystem, false);"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#else"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"EntityManager.GetSingleSystem<DataSenderSystem>()?.RegisterRequestProcessor<{part.GenericType},{secondType}>(currentSystem, false);"));
            unbindContainer.Tree.Add(new TabSimpleSyntax(3, $"#endif"));
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

        private void SetPrivateSingleComponentBinder(FieldDeclarationSyntax fieldDeclaration, string system, ISyntax fields, ISyntax binder, ISyntax unbinder)
        {
            var findComponent = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.componentOverData.ContainsKey(x.ToString()));

            var fieldType = findComponent?.ToString();

            if (fieldType == null)
            {
                fieldType = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.systemOverData.ContainsKey(x.ToString()))?.ToString();
            }

            if (fieldType == null) return;

            var fieldName = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is VariableDeclaratorSyntax).ToString();

            var fieldBindName = fieldName + "FieldSingleBinding";

            if (Program.systemOverData.ContainsKey(fieldType))
            {
                fields.Tree.Add(new TabSimpleSyntax(2, $"private FieldInfo {fieldBindName} = typeof({system}).GetField({CParse.Quote}{fieldName}{CParse.Quote}, BindingFlags.Instance | BindingFlags.NonPublic);"));
                binder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue({CurrentSystem}, {CurrentSystem}.Owner.World.GetSingleSystem<{fieldType}>());"));
                unbinder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue(system, null);"));
            }
            else
            {
                fields.Tree.Add(new TabSimpleSyntax(2, $"private FieldInfo {fieldBindName} = typeof({system}).GetField({CParse.Quote}{fieldName}{CParse.Quote}, BindingFlags.Instance | BindingFlags.NonPublic);"));
                binder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue({CurrentSystem}, {CurrentSystem}.Owner.World.GetSingleComponent<{fieldType}>());"));
                unbinder.Tree.Add(new TabSimpleSyntax(3, $"{fieldBindName}.SetValue(system, null);"));
            }
        }

        private void SetPublicComponentBinder(FieldDeclarationSyntax fieldDeclaration, string system, ISyntax binder, ISyntax unbinder)
        {
            var fieldType = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.componentOverData.ContainsKey(x.ToString())).ToString();
            var fieldName = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is VariableDeclaratorSyntax).ToString();

            binder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = {CurrentSystem}.Owner.GetOrAddComponent<{fieldType}>();"));
            unbinder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = null;"));
        }

        private void SetPublicSingleComponentBinder(FieldDeclarationSyntax fieldDeclaration, string system, ISyntax binder, ISyntax unbinder)
        {
            var fieldType = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.componentOverData.ContainsKey(x.ToString()))?.ToString();

            if (fieldType == null)
            {
                fieldType = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is IdentifierNameSyntax && Program.systemOverData.ContainsKey(x.ToString()))?.ToString();
            }

            if (fieldType == null) return;

            var fieldName = fieldDeclaration.DescendantNodes().FirstOrDefault(x => x is VariableDeclaratorSyntax).ToString();

            if (Program.systemOverData.ContainsKey(fieldType))
            {
                binder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = {CurrentSystem}.Owner.World.GetSingleSystem<{fieldType}>();"));
                unbinder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = null;"));
            }
            else
            {
                binder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = {CurrentSystem}.Owner.World.GetSingleComponent<{fieldType}>();"));
                unbinder.Tree.Add(new TabSimpleSyntax(3, $"{CurrentSystem}.{fieldName} = null;"));
            }
        }

        /// <summary>
        /// тут мы получаем контейнер для конкретной системы
        /// </summary>
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
                            currentUsings.AddUnique(GetNamespaces(a.ToString()));
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

        private ISyntax GetNamespaces(GenericNameSyntax genericNameSyntax)
        {
            var tree = new TreeSyntaxNode();

            tree.AddUnique(GetNamespaces(genericNameSyntax.Identifier.Text));

            foreach (var t in genericNameSyntax.TypeArgumentList.Arguments)
            {
                if (t is IdentifierNameSyntax identifier)
                {
                    tree.AddUnique(GetNamespaces(identifier.ToString()));
                }
                else if (t is GenericNameSyntax generic)
                {
                   tree.AddUnique(GetNamespaces(generic));
                }
            }
            return tree;
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
                if (c.Parent is NamespaceDeclarationSyntax namespaceDec)
                {
                    tree.Add(new UsingSyntax(namespaceDec.Name.ToString()));
                    continue;
                }

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


        #region CustomAndUniversalResolvers

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

                if (currentClass.BaseList != null && currentClass.BaseList.Types.Any(x => x.ToString() == ("IPredicate")))
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

        public List<(string, string)> GetAsyncActionsBluePrints()
        {
            var newList = new List<(string, string)>(2048);

            var count = Program.classes.Count;
            var classes = Program.classes;

            for (int i = 0; i < count; i++)
            {
                var currentClass = classes[i];

                if (currentClass.BaseList != null && currentClass.BaseList.Types.Any(x => x.ToString() == ("IAsyncAction")))
                {
                    newList.Add(($"{currentClass.Identifier.ValueText}Blueprint.cs", GetAsyncActionsBluePrintSyntax(currentClass).ToString()));
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
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("UnityEngine", 1));

            tree.Add(new TabSimpleSyntax(0, $"[CreateAssetMenu(fileName = {CParse.Quote}{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote}, menuName = {CParse.Quote}BluePrints/Actions/{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote})]"));
            tree.Add(new TabSimpleSyntax(0, $"public class {classDeclarationSyntax.Identifier.ValueText}Blueprint : ActionBluePrint<{classDeclarationSyntax.Identifier}>"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new RightScopeSyntax());
            return tree;
        }

        private ISyntax GetAsyncActionsBluePrintSyntax(ClassDeclarationSyntax classDeclarationSyntax)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("HECSFramework.Core"));
            tree.Add(new UsingSyntax("HECSFramework.Unity"));
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("UnityEngine", 1));

            tree.Add(new TabSimpleSyntax(0, $"[CreateAssetMenu(fileName = {CParse.Quote}{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote}, menuName = {CParse.Quote}BluePrints/AsyncActions/{classDeclarationSyntax.Identifier.ValueText}{CParse.Quote})]"));
            tree.Add(new TabSimpleSyntax(0, $"public class {classDeclarationSyntax.Identifier.ValueText}Blueprint : AsyncActionBluePrint<{classDeclarationSyntax.Identifier}>"));
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
                GetCommandResolver(t, resolvers);

            typeToIdDictionary.Add(new TabSimpleSyntax(2, "public Dictionary<Type, int> CommandsIDs = new Dictionary<Type, int>"));
            typeToIdDictionary.Add(new LeftScopeSyntax(2));
            typeToIdDictionary.Add(dictionaryBody);
            typeToIdDictionary.Add(new RightScopeSyntax(2, true));

            for (int i = 0; i < commands.Count; i++)
            {
                var t = commands[i];
                GetCommandMethod(t, dictionaryBody);
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
                if (c.TypeParameterList != null)
                {
                    ProcessGenericCommand(c, shortIDs);
                    continue;
                }

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

        private void ProcessGenericCommand(StructDeclarationSyntax structDeclarationSyntax, HashSet<ShortIDObject> shortIDObjects)
        {
            foreach (var attribute in structDeclarationSyntax.AttributeLists)
            {
                foreach (var a in attribute.Attributes)
                {
                    if (a.ToString().Contains(GenericNetworkCommand))
                    {
                        foreach (var dn in a.DescendantNodes())
                        {
                            if (dn is GenericNameSyntax nameSyntax)
                            {
                                var check = nameSyntax.ToString();

                                var shortIDdata = new ShortIDObject();

                                shortIDdata.Type = check;
                                shortIDdata.TypeCode = IndexGenerator.GenerateIndex(check);

                                if (structDeclarationSyntax.BaseList.ChildNodes().Any(x => x.ToString().Contains("INetworkCommand")))
                                {
                                    shortIDdata.DataType = 0;
                                }
                                else
                                {
                                    shortIDdata.DataType = 1;
                                }

                                shortIDObjects.Add(shortIDdata);
                            }
                        }
                    }
                }
            }
        }

        private HashSet<string> GetNetworkGenericTypes(StructDeclarationSyntax structDeclarationSyntax)
        {
            var newGenericTypes = new HashSet<string>(8);

            foreach (var attribute in structDeclarationSyntax.AttributeLists)
            {
                foreach (var a in attribute.Attributes)
                {
                    if (a.ToString().Contains(GenericNetworkCommand))
                    {
                        foreach (var dn in a.DescendantNodes())
                        {
                            if (dn is GenericNameSyntax nameSyntax)
                            {
                                var check = nameSyntax.ToString();
                                newGenericTypes.Add(check);
                                break;
                            }
                        }
                    }
                }
            }

            return newGenericTypes;
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

        private void GetCommandMethod(StructDeclarationSyntax command, TreeSyntaxNode dictionaryBody)
        {
            if (command.TypeParameterList == null)
                dictionaryBody.Add(new TabSimpleSyntax(3, $"{{typeof({command.Identifier.ValueText}), {IndexGenerator.GetIndexForType(command.Identifier.ValueText)}}},"));
            else
            {
                var hashSet = GetNetworkGenericTypes(command);

                foreach (var h in hashSet)
                    dictionaryBody.Add(new TabSimpleSyntax(3, $"{{typeof({h}), {IndexGenerator.GetIndexForType(h)}}},"));
            }
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

        private void GetCommandResolver(StructDeclarationSyntax type, TreeSyntaxNode treeSyntaxNode)
        {
            if (type.TypeParameterList == null)
                treeSyntaxNode.Add(new TabSimpleSyntax(3, $"{{{IndexGenerator.GetIndexForType(type.Identifier.ValueText)}, new CommandResolver<{type.Identifier.ValueText}>()}},"));
            else
            {
                var hashSet = GetNetworkGenericTypes(type);

                foreach (var h in hashSet)
                    treeSyntaxNode.Add(new TabSimpleSyntax(3, $"{{{IndexGenerator.GetIndexForType(h)}, new CommandResolver<{h}>()}},"));
            }
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