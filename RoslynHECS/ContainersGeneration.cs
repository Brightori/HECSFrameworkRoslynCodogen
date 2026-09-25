using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS;
using RoslynHECS.DataTypes;
using RoslynHECS.Helpers;

namespace HECSFramework.Core.Generator
{
    /// <summary>
    /// Генерация изолированных контейнеров типов: файл на тип вместо монолитов
    /// TypeProvider.cs / SystemBindings.cs / ComponentsWorldPart.cs / FastWorldPart.cs /
    /// MapResolver.cs / CustomAndUniversalResolvers.cs.
    /// См. docs/SPLIT_CONTAINERS_SPEC.md.
    /// </summary>
    public partial class CodeGenerator
    {
        public const string ContainerSuffix = "Container";
        public const string FastContainerSuffix = "FastContainer";
        public const string ResolverContainerSuffix = "ResolverContainer";
        public const string TypeContainersRegistry = "TypeContainersRegistry";

        /// <summary>
        /// Строка регистрации контейнера: статическое поле в рукописном TypeContainersRegistry
        /// (HECS.Core), инициализаторы всех partial-частей выполняются в его статическом конструкторе.
        /// </summary>
        private ISyntax GetRegistryRecord(string containerName)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new TabSimpleSyntax(1, $"internal static partial class {TypeContainersRegistry}"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"private static readonly bool {containerName} = Add(new {containerName}());"));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new ParagraphSyntax());
            return tree;
        }

        #region ComponentContainer
        public string GetComponentContainer(LinkedNode component, bool withResolvers)
        {
            var name = component.Name;
            var hash = IndexGenerator.GetIndexForType(name);

            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();

            tree.Add(usings);
            usings.AddUnique(new UsingSyntax("System"));
            usings.AddUnique(new UsingSyntax("Components"));

            if (withResolvers)
            {
                usings.AddUnique(new UsingSyntax("MessagePack"));
                AddNamespaces(usings, name);
                usings.Add(new ParagraphSyntax());

                //partial-объявления IData мержатся; ключ Union = TypeHashCode типа — стабилен и локален.
                //атрибут стоит вне неймспейса, а резолвер живёт в DefaultNameSpace — имя только полное
                tree.Add(new TabSimpleSyntax(0, $"[Union({hash}, typeof({DefaultNameSpace}.{name}{Resolver}))]"));
                tree.Add(new TabSimpleSyntax(0, "public partial interface IData { }"));
                tree.Add(new ParagraphSyntax());
            }
            else
            {
                AddNamespaces(usings, name);
                usings.Add(new ParagraphSyntax());
            }

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(GetRegistryRecord(name + ContainerSuffix));

            //сам контейнер: данные типа, провайдер компонента для мира и регистрация его резолверов
            var interfaces = withResolvers ? "IComponentContainer, IResolverContainer" : "IComponentContainer";
            tree.Add(new TabSimpleSyntax(1, $"public sealed class {name}{ContainerSuffix} : {interfaces}"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"public Type ComponentType => typeof({name});"));
            tree.Add(new TabSimpleSyntax(2, $"public int TypeHashCode => {hash};"));
            tree.Add(new TabSimpleSyntax(2, $"public IComponent Factory() => new {name}();"));
            tree.Add(new TabSimpleSyntax(2, $"public void RegisterWorld(World world) => ComponentProvider<{name}>.RegisterWorld(world);"));

            if (withResolvers)
                tree.Add(new TabSimpleSyntax(2, $"public void RegisterResolvers(ResolversMap map) => map.RegisterResolver_{name}();"));

            tree.Add(new RightScopeSyntax(1));

            //часть ResolversMap: сериализация компонента (бывшие switch-и MapResolver.cs)
            if (withResolvers)
            {
                tree.Add(new ParagraphSyntax());
                tree.Add(GetComponentResolverRegistration(name, hash));
            }

            tree.Add(new RightScopeSyntax());
            return tree.ToString();
        }

        private ISyntax GetComponentResolverRegistration(string name, int hash)
        {
            var tree = new TreeSyntaxNode();
            var resolver = name + Resolver;

            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"internal void RegisterResolver_{name}()"));
            tree.Add(new LeftScopeSyntax(2));

            //упаковка компонента в контейнер (бывший GetContainerForComponentFuncProvider)
            tree.Add(new TabSimpleSyntax(3, $"packComponentByHash.Add({hash}, (component) =>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, $"var casted = ({name})component;"));
            tree.Add(new TabSimpleSyntax(4, $"return PackComponentToContainer(component, new {resolver}().In(ref casted));"));
            tree.Add(new TabSimpleSyntax(3, "});"));

            //применение контейнера к энтити по гуиду (бывший ProcessComponents)
            tree.Add(new TabSimpleSyntax(3, $"processComponentByHash.Add({hash}, (ref {ResolverContainer} container, int worldIndex) =>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, $"var resolver = MessagePackSerializer.Deserialize<{resolver}>(container.Data);"));
            tree.Add(new TabSimpleSyntax(4, "if (EntityManager.TryGetEntityByID(container.EntityGuid, out var entity))"));
            tree.Add(new LeftScopeSyntax(4));
            tree.Add(new TabSimpleSyntax(5, $"var component = entity.GetOrAddComponent<{name}>();"));
            tree.Add(new TabSimpleSyntax(5, "resolver.Out(ref component);"));
            tree.Add(new RightScopeSyntax(4));
            tree.Add(new TabSimpleSyntax(3, "});"));

            //применение контейнера к конкретной энтити (бывший ProcessResolverContainerRealisation)
            tree.Add(new TabSimpleSyntax(3, $"resolverToEntityByHash.Add({hash}, (ref {ResolverContainer} container, ref Entity entity) =>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, $"var resolver = MessagePackSerializer.Deserialize<{resolver}>(container.Data);"));
            tree.Add(new TabSimpleSyntax(4, $"var component = entity.GetOrAddComponent<{name}>();"));
            tree.Add(new TabSimpleSyntax(4, "resolver.Out(ref component);"));
            tree.Add(new TabSimpleSyntax(3, "});"));

            //создание компонента из контейнера (бывший GetComponentFromContainerFuncRealisation)
            tree.Add(new TabSimpleSyntax(3, $"componentFromContainerByHash.Add({hash}, (container) =>"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, $"var component = new {name}();"));
            tree.Add(new TabSimpleSyntax(4, $"var resolver = MessagePackSerializer.Deserialize<{resolver}>(container.Data);"));
            tree.Add(new TabSimpleSyntax(4, "resolver.Out(ref component);"));
            tree.Add(new TabSimpleSyntax(4, "return component;"));
            tree.Add(new TabSimpleSyntax(3, "});"));

            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            return tree;
        }
        #endregion

        #region SystemContainer
        public string GetSystemContainerFile(LinkedNode system)
        {
            var name = system.Name;
            var hash = IndexGenerator.GetIndexForType(name);

            var tree = new TreeSyntaxNode();
            var usingSpaces = new TreeSyntaxNode();

            //тот же набор, что был у SystemBindings.cs; кладём в usingSpaces, чтобы using
            //из generic-аргументов реактов дедуплицировались с захардкоженными
            usingSpaces.AddUnique(new UsingSyntax("System"));
            usingSpaces.AddUnique(new UsingSyntax("Components"));
            usingSpaces.AddUnique(new UsingSyntax("Commands"));
            usingSpaces.AddUnique(new UsingSyntax("Systems"));
            usingSpaces.AddUnique(new UsingSyntax("UnityEngine"));
            usingSpaces.AddUnique(new UsingSyntax("Cysharp.Threading.Tasks"));
            tree.Add(usingSpaces);
            tree.Add(new UsingSyntax("System.Reflection", 1));

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());

            tree.Add(GetRegistryRecord(name + ContainerSuffix));

            var fields = new TreeSyntaxNode();
            var systemPlace = new TreeSyntaxNode();
            var bindBody = new TreeSyntaxNode();
            var unbindBody = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(1, $"public sealed class {name}{ContainerSuffix} : ISystemContainer"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(fields);
            tree.Add(new TabSimpleSyntax(2, $"public Type SystemType => typeof({name});"));
            tree.Add(new TabSimpleSyntax(2, $"public int TypeHashCode => {hash};"));
            tree.Add(new TabSimpleSyntax(2, $"public ISystem Factory() => new {name}();"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "public void BindSystem(ISystem system)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(systemPlace);
            tree.Add(bindBody);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "public void UnBindSystem(ISystem system)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(systemPlace);
            tree.Add(unbindBody);
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            FillSystemBindings(system, bindBody, unbindBody, systemPlace, fields, usingSpaces);

            return tree.ToString();
        }

        /// <summary>
        /// Тело биндингов одной системы: реакты по интерфейсам + [Required]/[Single] поля.
        /// Повторяет логику цикла из GetContaineresForSystems, но для одного файла.
        /// </summary>
        private void FillSystemBindings(LinkedNode system, TreeSyntaxNode bindBody, TreeSyntaxNode unbindBody,
            TreeSyntaxNode systemPlace, TreeSyntaxNode fields, TreeSyntaxNode usingSpaces)
        {
            interfaceGenericCache.Clear();
            systemCasheParentsAndPartial.Clear();

            system.GetGenericInterfaces(interfaceGenericCache);
            system.GetAllParentsAndParts(systemCasheParentsAndPartial);

            foreach (var interfaceType in interfaceGenericCache)
            {
                ProcessReacts(interfaceType, bindBody, unbindBody, system, usingSpaces);
            }

            foreach (var systemPart in systemCasheParentsAndPartial)
            {
                var attributes = systemPart.DescendantNodes().OfType<AttributeListSyntax>();

                if (attributes == null)
                    continue;

                foreach (var attribute in attributes)
                {
                    if (attribute.Attributes.Any(x => x.IsAttribute("Required")))
                    {
                        if (attribute.Parent is FieldDeclarationSyntax field)
                        {
                            if (field.Modifiers.Any(x => x.ToString().Contains("private") || x.ToString().Contains("protected")))
                                SetPrivateComponentBinder(field, systemPart.Identifier.ValueText, fields, bindBody, unbindBody);
                            else
                                SetPublicComponentBinder(field, systemPart.Identifier.ValueText, bindBody, unbindBody);
                        }
                    }

                    if (attribute.Attributes.Any(x => x.IsAttribute("Single")))
                    {
                        if (attribute.Parent is FieldDeclarationSyntax field)
                        {
                            if (field.Modifiers.Any(x => x.ToString().Contains("private") || x.ToString().Contains("protected")))
                                SetPrivateSingleComponentBinder(field, systemPart.Identifier.ValueText, fields, bindBody, unbindBody);
                            else
                                SetPublicSingleComponentBinder(field, systemPart.Identifier.ValueText, bindBody, unbindBody);
                        }
                    }
                }
            }

            //если есть что биндить — добавляем каст системы к нужному типу (общий для Bind и UnBind)
            if (bindBody.Tree.Count > 0)
                systemPlace.Tree.Add(new TabSimpleSyntax(3, $"var {CurrentSystem} = ({system.Name})system;"));
        }
        #endregion

        #region FastComponentContainer
        public string GetFastComponentContainer(StructDeclarationSyntax fastComponent)
        {
            var name = fastComponent.Identifier.ValueText;

            var containerName = name + FastContainerSuffix;

            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Components", 1));
            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());
            tree.Add(GetRegistryRecord(containerName));
            tree.Add(new TabSimpleSyntax(1, $"public sealed class {containerName} : IFastComponentContainer"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"public void RegisterWorld(World world) => FastComponentProvider<{name}>.RegisterWorld(world);"));
            tree.Add(new TabSimpleSyntax(2, $"public void UnRegisterWorld(World world) => FastComponentProvider<{name}>.UnRegisterWorld(world);"));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }
        #endregion

        #region ResolversMapRuntime
        /// <summary>
        /// Стабильный файл: инфраструктура ResolversMap (бывший MapResolver.cs).
        /// Словари по хешам вместо switch-ей; наполнение — контейнерами IResolverContainer из TypesMap.
        /// </summary>
        public string GetResolversMapRuntime()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("System"));
            tree.Add(new UsingSyntax("System.Collections.Generic"));
            tree.Add(new UsingSyntax("Components"));
            tree.Add(new UsingSyntax("MessagePack.Resolvers"));
            tree.Add(new UsingSyntax("MessagePack", 1));

            tree.Add(new TabSimpleSyntax(0, "public partial interface IData { }"));
            tree.Add(new ParagraphSyntax());

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));

            tree.Add(new TabSimpleSyntax(2, $"public delegate void ProcessComponentByHashDelegate(ref {ResolverContainer} container, int worldIndex);"));
            tree.Add(new TabSimpleSyntax(2, $"public delegate void ResolverToEntityDelegate(ref {ResolverContainer} container, ref Entity entity);"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private Dictionary<int, Func<IComponent, {ResolverContainer}>> packComponentByHash = new Dictionary<int, Func<IComponent, {ResolverContainer}>>(512);"));
            tree.Add(new TabSimpleSyntax(2, "private Dictionary<int, ProcessComponentByHashDelegate> processComponentByHash = new Dictionary<int, ProcessComponentByHashDelegate>(512);"));
            tree.Add(new TabSimpleSyntax(2, "private Dictionary<int, ResolverToEntityDelegate> resolverToEntityByHash = new Dictionary<int, ResolverToEntityDelegate>(512);"));
            tree.Add(new TabSimpleSyntax(2, $"private Dictionary<int, Func<{ResolverContainer}, IComponent>> componentFromContainerByHash = new Dictionary<int, Func<{ResolverContainer}, IComponent>>(512);"));

            //конструктор: та же последовательность, что была у генерируемого MapResolver.cs
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "public ResolversMap()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "GetComponentContainerFunc = GetContainerForComponentFuncProvider;"));
            tree.Add(new TabSimpleSyntax(3, "ProcessResolverContainer = ProcessResolverContainerRealisation;"));
            tree.Add(new TabSimpleSyntax(3, "GetComponentFromContainer = GetComponentFromContainerFuncRealisation;"));
            tree.Add(new TabSimpleSyntax(3, "InitCustomResolversDictionaries();"));
            tree.Add(new TabSimpleSyntax(3, "CollectRegistrations();"));
            tree.Add(new TabSimpleSyntax(3, "InitPartialCommandResolvers();"));
            tree.Add(new TabSimpleSyntax(3, "JSONModuleInit();"));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(2, "partial void JSONModuleInit();"));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private void CollectRegistrations()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "foreach (var container in TypesMap.GetContainers<IResolverContainer>())"));
            tree.Add(new TabSimpleSyntax(4, "container.RegisterResolvers(this);"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, "private void InitCustomResolversDictionaries()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "typeToCustomResolver = new Dictionary<Type, CustomResolverProviderBase>(64);"));
            tree.Add(new TabSimpleSyntax(3, "typeCodeToCustomResolver = new Dictionary<int, CustomResolverProviderBase>(64);"));
            tree.Add(new TabSimpleSyntax(3, "getTypeIndexToType = new Dictionary<int, Type>(64);"));
            tree.Add(new RightScopeSyntax(2));

            //реализации функций — теперь через словари вместо switch
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private {ResolverContainer} GetContainerForComponentFuncProvider<T>(T component) where T : IComponent"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "if (packComponentByHash.TryGetValue(component.GetTypeHashCode, out var pack))"));
            tree.Add(new TabSimpleSyntax(4, "return pack(component);"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(3, "return default;"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"partial void LoadDataFromContainerSwitch({ResolverContainer} dataContainerForResolving, int worldIndex)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "switch (dataContainerForResolving.Type)"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(new TabSimpleSyntax(4, "case 0:"));
            tree.Add(new TabSimpleSyntax(5, "ProcessComponents(ref dataContainerForResolving, worldIndex);"));
            tree.Add(new TabSimpleSyntax(5, "break;"));
            tree.Add(new RightScopeSyntax(3));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private void ProcessComponents(ref {ResolverContainer} dataContainerForResolving, int worldIndex)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "if (processComponentByHash.TryGetValue(dataContainerForResolving.TypeHashCode, out var process))"));
            tree.Add(new TabSimpleSyntax(4, "process(ref dataContainerForResolving, worldIndex);"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private void ProcessResolverContainerRealisation(ref {ResolverContainer} dataContainerForResolving, ref Entity entity)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "if (resolverToEntityByHash.TryGetValue(dataContainerForResolving.TypeHashCode, out var process))"));
            tree.Add(new TabSimpleSyntax(4, "process(ref dataContainerForResolving, ref entity);"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(2, $"private IComponent GetComponentFromContainerFuncRealisation({ResolverContainer} resolverDataContainer)"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "if (componentFromContainerByHash.TryGetValue(resolverDataContainer.TypeHashCode, out var fromContainer))"));
            tree.Add(new TabSimpleSyntax(4, "return fromContainer(resolverDataContainer);"));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(3, "return default;"));
            tree.Add(new RightScopeSyntax(2));

            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());
            return tree.ToString();
        }
        #endregion

        #region CustomResolverRegistration
        /// <summary>
        /// Регистрация кастомного резолвера ([HECSManualResolver]/[HECSResolver]) — файл на тип.
        /// Бывшие словари CustomAndUniversalResolvers.cs.
        /// </summary>
        public string GetCustomResolverRegistration(string typeName, ResolverData resolverData)
        {
            var hash = IndexGenerator.GenerateIndex(typeName);
            var containerName = typeName + ResolverContainerSuffix;

            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();
            tree.Add(usings);

            usings.AddUnique(new UsingSyntax("System"));
            usings.AddUnique(new UsingSyntax("System.Collections.Generic"));

            //тип может быть классом, структурой или enum'ом — единая таблица покрывает всё
            AddNamespaces(usings, typeName);
            usings.Add(new ParagraphSyntax());

            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());
            tree.Add(GetRegistryRecord(containerName));
            tree.Add(new TabSimpleSyntax(1, $"public sealed class {containerName} : IResolverContainer"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"public void RegisterResolvers(ResolversMap map) => map.RegisterCustom_{typeName}();"));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new ParagraphSyntax());
            tree.Add(new TabSimpleSyntax(1, "public partial class ResolversMap"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(new TabSimpleSyntax(2, $"internal void RegisterCustom_{typeName}()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, $"typeToCustomResolver.Add(typeof({typeName}), new CustomResolverProvider<{typeName}, {resolverData.ResolverName}>());"));
            tree.Add(new TabSimpleSyntax(3, $"typeCodeToCustomResolver.Add({hash}, new CustomResolverProvider<{typeName}, {resolverData.ResolverName}>());"));
            tree.Add(new TabSimpleSyntax(3, $"getTypeIndexToType.Add({hash}, typeof({typeName}));"));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax(1));
            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }

        /// <summary>
        /// Универсальный резолвер ([HECSResolver]) — класс резолвера в отдельном файле.
        /// Бывшая часть CustomAndUniversalResolvers.cs.
        /// </summary>
        public string GetUniversalResolverFile(LinkedNode node)
        {
            var tree = new TreeSyntaxNode();
            var usings = new TreeSyntaxNode();
            tree.Add(usings);

            usings.AddUnique(new UsingSyntax("System"));
            usings.AddUnique(new UsingSyntax("System.Collections.Generic"));

            var body = new TreeSyntaxNode();
            tree.Add(new NameSpaceSyntax(DefaultNameSpace));
            tree.Add(new LeftScopeSyntax());
            tree.Add(body);
            tree.Add(new RightScopeSyntax());

            body.Add(GetUniversalResolver(node, usings));
            usings.Add(new ParagraphSyntax());

            return tree.ToString();
        }
        #endregion
    }
}
