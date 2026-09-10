using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynHECS.Helpers
{
    /// <summary>
    /// хелпер для сравнения имён аттрибутов, тут мы не зависим от того,
    /// написан ли аттрибут с пустыми скобками, с суффиксом Attribute или через полный неймспейс
    /// </summary>
    public static class AttributeHelper
    {
        private const string AttributeSuffix = "Attribute";

        /// <summary>
        /// [Foo], [Foo()], [FooAttribute], [Some.Name.Space.Foo] - всё это IsAttribute("Foo")
        /// </summary>
        public static bool IsAttribute(this AttributeSyntax attribute, string name)
        {
            if (attribute == null)
                return false;

            return attribute.GetAttributeName() == TrimAttributeSuffix(name);
        }

        /// <summary>
        /// короткое имя аттрибута без квалификации, аргументов и суффикса Attribute
        /// </summary>
        public static string GetAttributeName(this AttributeSyntax attribute)
        {
            if (attribute == null)
                return string.Empty;

            var name = attribute.Name;

            while (true)
            {
                if (name is QualifiedNameSyntax qualified)
                {
                    name = qualified.Right;
                    continue;
                }

                if (name is AliasQualifiedNameSyntax aliasQualified)
                {
                    name = aliasQualified.Name;
                    continue;
                }

                break;
            }

            var shortName = name is SimpleNameSyntax simpleName ? simpleName.Identifier.ValueText : name.ToString();
            return TrimAttributeSuffix(shortName);
        }

        private static string TrimAttributeSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            if (name.Length > AttributeSuffix.Length && name.EndsWith(AttributeSuffix))
                return name.Substring(0, name.Length - AttributeSuffix.Length);

            return name;
        }
    }
}
