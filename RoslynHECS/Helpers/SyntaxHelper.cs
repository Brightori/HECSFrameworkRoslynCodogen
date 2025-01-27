﻿using System.Linq;
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
            foreach (var s in add.Tree)
            {
                if (syntax.Tree.Any(t => t.ToString() == s.ToString()))
                    continue;

                syntax.Tree.Add(s); 
            }

            //syntax.Tree.Add(add);
        }
    }
}