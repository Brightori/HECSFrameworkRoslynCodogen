using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynHECS;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
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
