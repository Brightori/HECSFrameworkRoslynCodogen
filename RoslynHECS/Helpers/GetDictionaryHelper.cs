using HECSFramework.Core.Generator;

namespace RoslynHECS.Helpers
{
    internal static class GetDictionaryHelper
    {
        public static ISyntax GetDictionaryMethod(string dictionaryName, string key, string value, int initialIndent, out ISyntax dictionaryBody)
        {
            var tree = new TreeSyntaxNode();
            var body = new TreeSyntaxNode();
            dictionaryBody = body;

            tree.Add(new TabSimpleSyntax(initialIndent,
                $"private Dictionary<{key},{value}> {dictionaryName}()"));

            tree.Add(new LeftScopeSyntax(initialIndent));
            tree.Add(new TabSimpleSyntax(initialIndent+1, $"return new Dictionary<{key},{value}>"));
            tree.Add(new LeftScopeSyntax(initialIndent+1));
            tree.Add(body);
            tree.Add(new RightScopeSyntax(initialIndent + 1, true));
            tree.Add(new RightScopeSyntax(initialIndent));
            return tree;
        }

        public static ISyntax DictionaryBodyRecord(int intend, string key, string value)
        {
            return new TabSimpleSyntax(intend, $"{CParse.LeftScope} {key}{CParse.Comma} {value} {CParse.RightScope}{CParse.Comma}");
        }
    }
}