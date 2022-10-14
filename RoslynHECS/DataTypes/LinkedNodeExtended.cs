using System.Collections.Generic;
using System.Linq;
using HECSFramework.Core.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ClassDeclarationSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;

namespace RoslynHECS.DataTypes
{
    //we have members here
    public sealed class LinkedNodeExtended : LinkedNode
    {
        public HashSet<AttributeSyntax> ClassAttributes = new HashSet<AttributeSyntax>();
        public HashSet<MemberNode> MemberDeclarationSyntaxes = new HashSet<MemberNode>();
        public HashSet<AttributeSyntax> PartialSerializaion = new HashSet<AttributeSyntax>();

        public bool IsPrivateFieldIncluded { get; private set; }
        public bool IsPartialSerialization => PartialSerializaion.Count > 0;

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

            IsPrivateFieldIncluded = MemberDeclarationSyntaxes.Any(x => x.IsSerializable && !x.IsPublic());
            PartialSerializaion = ClassAttributes.Where(x => x.Name.ToString() == "PartialSerializeField").ToHashSet();
        }

        public IEnumerable<GatheredField> GetPartialSerializationFields()
        {
            foreach (var field in PartialSerializaion)
            {
                var arguments = field.ArgumentList.Arguments.ToArray();

                if (arguments == null || arguments.Length == 0)
                    continue;

                var 

                yield return new GatheredField()
            }
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
}