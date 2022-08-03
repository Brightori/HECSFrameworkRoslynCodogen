using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynHECS.Helpers
{
    public static class LinkedNodeHelper
    {
        public static LinkedNode GetLinkedNode(ClassDeclarationSyntax obj, Dictionary<string, LinkedNode> nodesCollection)
        {
            if (nodesCollection.ContainsKey(obj.Identifier.ValueText))
                return nodesCollection[obj.Identifier.ValueText];   

            var name = obj.Identifier.ValueText;
            var linkedNode = new LinkedNode()
            {
                Name = obj.Identifier.ValueText,
                ClassDeclaration = obj,
                Parent = null,
                IsAbstract = obj.Modifiers.Any(x => x.ValueText == "abstract"),
                IsPartial = obj.Modifiers.Any(x => x.ValueText == "partial"),
                IsGeneric = obj.TypeParameterList != null,
                Parts = new HashSet<ClassDeclarationSyntax>(),
                Interfaces = new HashSet<LinkedInterfaceNode>(),
            };

            var parts = Program.classes.Where(x => x.Identifier.ValueText == name);

            if (linkedNode.IsPartial)
            {
                foreach (var part in parts)
                {
                    if (part == obj) continue;
                    linkedNode.Parts.Add(part);

                    if (Program.interfacesOverData.TryGetValue(part.ToString(), out var node))
                    {
                        linkedNode.Interfaces.Add(node);
                    }
                }
            }

            if (obj.BaseList != null)
            {
                foreach (var b in obj.BaseList.Types) 
                { 
                    if (Program.classesByName.TryGetValue(b.ToString(), out var needed))
                    {
                        linkedNode.Parent = GetLinkedNode(needed, nodesCollection);
                        break;
                    }
                }
            }

            nodesCollection.TryAdd(name, linkedNode);
            return linkedNode;
        }
    }
}