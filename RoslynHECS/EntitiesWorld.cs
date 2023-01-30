using RoslynHECS;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
        public string GetEntitiesWorldPart()
        {
            var tree = new TreeSyntaxNode();
            tree.Add(new UsingSyntax("Components", 1));
            tree.Add(new NameSpaceSyntax("HECSFramework.Core"));
            tree.Add(new LeftScopeSyntax());

            tree.Add(new TabSimpleSyntax(1, "public partial class World"));
            tree.Add(new LeftScopeSyntax(1));
            tree.Add(FillStandartComponentRegistrators());
            tree.Add(new RightScopeSyntax(1));

            tree.Add(new RightScopeSyntax());

            return tree.ToString();
        }

        private ISyntax FillStandartComponentRegistrators()
        {
            var tree = new TreeSyntaxNode();
            var body = new TreeSyntaxNode();

            tree.Add(new TabSimpleSyntax(2, "partial void FillStandartComponentRegistrators()"));
            tree.Add(new LeftScopeSyntax(2));
            tree.Add(new TabSimpleSyntax(3, "componentProviderRegistrators = new ComponentProviderRegistrator[]"));
            tree.Add(new LeftScopeSyntax(3));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(3, true));
            tree.Add(new RightScopeSyntax(2));


            foreach (var c in Program.componentOverData)
            {
                if (c.Value.IsAbstract)
                    continue;

                body.Add(new TabSimpleSyntax(4, $"new ComponentProviderRegistrator<{c.Value.Name}>(),"));
            }

            return tree;
        }
    }

   
}
