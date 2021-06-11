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
    }
}
