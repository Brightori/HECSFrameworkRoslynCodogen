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

        public bool IsPrivateFieldIncluded { get; private set; }

        public LinkedNodeExtended(LinkedNode linkedNode)
        {
            Name = linkedNode.Name;
            ClassDeclaration = linkedNode.ClassDeclaration;
            Parent = linkedNode;
            IsAbstract = linkedNode.IsAbstract;
            IsPartial = linkedNode.IsPartial;
            IsGeneric = linkedNode.IsGeneric;

            Parts = linkedNode.Parts;
            Interfaces = linkedNode.Interfaces;

            if (linkedNode.Parent != null)
            {
                foreach (var p in linkedNode.GetParents())
                {
                    if (p == null)
                        continue;

                    ProcessClass(p.ClassDeclaration);
                }
            }

            foreach (var p in linkedNode.Parts)
                ProcessClass(p);

            IsPrivateFieldIncluded = ClassAttributes.Any(x => x.Name.ToString() == "PrivateFieldsIncluded");
        }

        private void ProcessClass(ClassDeclarationSyntax classDeclarationSyntax)
        {
            ClassAttributes = classDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToHashSet();

            foreach (var m in classDeclarationSyntax.Members)
            {
                if (m is PropertyDeclarationSyntax || m is FieldDeclarationSyntax)
                {
                    MemberDeclarationSyntaxes.Add(new MemberNode(m));
                }
            }
        }
    }

    public class MemberNode
    {
        public MemberDeclarationSyntax MemberDeclarationSyntax;
        public HashSet<AttributeSyntax> Attributes = new HashSet<AttributeSyntax>();
        public bool IsHaveAttributes => Attributes.Count > 0;

        public MemberNode(MemberDeclarationSyntax memberDeclarationSyntax)
        {
            MemberDeclarationSyntax = memberDeclarationSyntax;
            Attributes = memberDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToHashSet();
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