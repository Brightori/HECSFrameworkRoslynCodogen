using System;
using System.Collections.Generic;
using System.Linq;
using HECSFramework.Core.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynHECS.DataTypes
{
    public class MemberNode
    {
        public MemberDeclarationSyntax MemberDeclarationSyntax;
        public HashSet<AttributeSyntax> Attributes = new HashSet<AttributeSyntax>();

        public bool IsHaveAttributes => Attributes.Count > 0;
        public bool IsProperty => MemberDeclarationSyntax is PropertyDeclarationSyntax;
        public bool IsField => MemberDeclarationSyntax is FieldDeclarationSyntax;

        public bool IsSerializable => GatheredField.IsSerializable;
        public GatheredField GatheredField;

        public MemberNode(MemberDeclarationSyntax memberDeclarationSyntax)
        {
            MemberDeclarationSyntax = memberDeclarationSyntax;
            Attributes = memberDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToHashSet();
            GatheredField = new GatheredField(this);
        }

        public override bool Equals(object obj)
        {
            return obj is MemberNode node &&
                   EqualityComparer<MemberDeclarationSyntax>.Default.Equals(MemberDeclarationSyntax, node.MemberDeclarationSyntax);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MemberDeclarationSyntax);
        }

        public bool IsPublic()
        {
            if (MemberDeclarationSyntax is PropertyDeclarationSyntax property)
            {
                var t = property.AccessorList?.Accessors.FirstOrDefault(x => x.Keyword.Text == "set");

                if (t == null || t.Modifiers.Any(x => x.IsKind(SyntaxKind.PrivateKeyword) || x.IsKind(SyntaxKind.ProtectedKeyword)))
                    return false;
                else
                    return true;
            }

            if (MemberDeclarationSyntax is FieldDeclarationSyntax field)
                return MemberDeclarationSyntax.Modifiers.ToString().Contains("public");

            return false;
        }
    }
}