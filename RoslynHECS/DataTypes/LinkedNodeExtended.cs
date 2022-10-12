using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ClassDeclarationSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;

namespace RoslynHECS.DataTypes
{
    //we have members here
    public sealed class LinkedNodeExtended : LinkedNode
    {
        public HashSet<AttributeSyntax> ClassAttributes = new HashSet<AttributeSyntax>();
        public HashSet<MemberNode> MemberDeclarationSyntaxes = new HashSet<MemberNode>();

        public LinkedNodeExtended(LinkedNode linkedNode)
        {
            foreach (var p in linkedNode.GetParents())
                ProcessClass(p.ClassDeclaration);

            foreach (var p in linkedNode.Parts)
                ProcessClass(p);
        }

        private void ProcessClass(ClassDeclarationSyntax classDeclarationSyntax)
        {
            ClassAttributes = classDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToHashSet();

            foreach (var m in classDeclarationSyntax.Members)
            {
                if (m is PropertyDeclarationSyntax || m is FieldDeclarationSyntax)
                {
                    MemberDeclarationSyntaxes.Add(new MemberNode(classDeclarationSyntax));
                }
            }
        }
    }

    public class MemberNode
    {
        public MemberDeclarationSyntax MemberDeclarationSyntax;
        public HashSet<AttributeSyntax> ClassAttributes = new HashSet<AttributeSyntax>();
        public bool IsHaveAttributes => ClassAttributes.Count > 0;

        public MemberNode (MemberDeclarationSyntax memberDeclarationSyntax)
        {
            MemberDeclarationSyntax = memberDeclarationSyntax;
            ClassAttributes = memberDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToHashSet();
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
    }
}