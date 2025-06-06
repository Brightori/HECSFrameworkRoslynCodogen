using System.Linq;
using System.Runtime.CompilerServices;
using HECSFramework.Core.Generator;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynHECS.Helpers
{
    public static class SyntaxHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetType(MemberDeclarationSyntax memberDeclarationSyntax)
        {
            if (memberDeclarationSyntax is PropertyDeclarationSyntax property)
                return property.Type.ToString();
            else if (memberDeclarationSyntax is FieldDeclarationSyntax field)
                return field.Declaration.Type.ToString();

            return string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetFieldName(MemberDeclarationSyntax memberDeclarationSyntax)
        {
            if (memberDeclarationSyntax is PropertyDeclarationSyntax property)
                return property.Identifier.ToString();
            else if (memberDeclarationSyntax is FieldDeclarationSyntax field)
                return field.Declaration.Variables[0].Identifier.ToString();

            return string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddUnique(this ISyntax syntax, ISyntax add)
        {
            var syntaxToString = syntax.ToString();

            if (add.Tree == null || add.Tree.Count == 0)
            {
                var addToString = add.ToString();

                if (string.IsNullOrEmpty(addToString))
                    return;

                if (syntaxToString.Contains(add.ToString()))
                    return;

                syntax.Tree.Add(add);
                return;
            }

            foreach (var item in add.Tree)
            {
                if (syntaxToString.Contains(item.ToString()))
                    return;

                syntax.AddUnique(item);
            }
        }
    }
}