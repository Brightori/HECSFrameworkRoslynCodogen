using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
        public string GetFastWorldPart()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Components",1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());
            
            tree.Add(new TabSimpleSyntax(1, "public partial class World"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(GetFillTypeRegistrator());
            tree.Add(new RightScopeSyntax(1));

            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }      

        private ISyntax FillComponentToResolver()
        {
            var tree = new TreeSyntaxNode();

            foreach (var component in containersSolve)
            {
                var compToString = component.Identifier.ValueText;
                tree.Add(new TabSimpleSyntax(4, $"{CParse.LeftScope} typeof({component.Identifier.ValueText}), new ComponentToResolver<{compToString}, {compToString}{Resolver}>() {CParse.RightScope},"));
            }

            return tree;
        }

        private ISyntax GetFillTypeRegistrator()
        {
            var tree = new TreeSyntaxNode();
            var body = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "partial void FillTypeRegistrators()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "typeRegistrators = new TypeRegistrator[]"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(3,true));
            tree.Add(new RightScopeSyntax(2));

            for (int i = 0; i < Program.fastComponents.Count; i++)
            {
                body.Add(new TabSimpleSyntax(4, $"new TypeRegistrator<{Program.fastComponents[i].Identifier.ValueText}>(),"));
            }         

            return tree;
        }

        public List<(string fileName,string data)> GetProvidersForFastComponent()
        {
            var list = new List<(string fileName, string data)>(512);

            foreach (var fc in Program.fastComponents)
            {
                list.Add(($"{fc.Identifier.ValueText}FastProvider.cs", GetFastComponentProviderBody(fc).ToString()));
            }

            return list;
        }

        private ISyntax GetFastComponentProviderBody(StructDeclarationSyntax structDeclarationSyntax)
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("HECSFramework.Core"));
            tree.Add(new UsingSyntax("HECSFramework.Unity", 1));

            tree.Add(new NameSpaceSyntax("Components"));
            tree.Add(new LeftScopeSyntax());
            tree.Add(new TabSimpleSyntax(1, $"public class {structDeclarationSyntax.Identifier.ValueText}FastProvider : FastComponentMonoProvider<{structDeclarationSyntax.Identifier.ValueText}>"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new RightScopeSyntax(2));
            tree.Add(new RightScopeSyntax());

            return tree;
        }
    }
}
