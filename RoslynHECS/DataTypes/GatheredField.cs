using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using RoslynHECS.DataTypes;
using RoslynHECS.Helpers;

namespace HECSFramework.Core.Generator
{
    public struct GatheredField
    {
        public string Type;
        public string FieldName;
        public CSharpSyntaxNode Node;

        public int Order;
        public string ResolverName;
        public bool IsPrivate;
        public bool IsSerializable;
        public bool IsPartial;
        public string Namespace;

        public GatheredField(MemberNode memberNode) : this()
        {
            Type = SyntaxHelper.GetType(memberNode.MemberDeclarationSyntax);
            FieldName = SyntaxHelper.GetFieldName(memberNode.MemberDeclarationSyntax);
            Node = memberNode.MemberDeclarationSyntax;

            if (memberNode.Attributes.Count > 0)
            {
                var needed = memberNode.Attributes.FirstOrDefault(x => x.Name.ToString() == "Field");

                if (needed != null)
                {
                    if (needed.ArgumentList != null)
                    {
                        var resolver = string.Empty;

                        var arguments = needed.ArgumentList.Arguments.ToArray();
                        Order = int.Parse(arguments[0].ToString());

                        if (arguments.Length > 1)
                        {
                            var data = arguments[1].ToString();
                            data = data.Replace("typeof(", "");
                            data = data.Replace(")", "");
                            ResolverName = data;
                        }
                    }

                    IsSerializable = true;
                }
            }

            if (memberNode.IsPublic())
                IsPrivate = false;
            else
                IsPrivate = true;
        }

        public override bool Equals(object obj)
        {
            return obj is GatheredField field &&
                   Type == field.Type &&
                   FieldName == field.FieldName &&
                   Order == field.Order;
        }

        public override int GetHashCode()
        {
            int hashCode = -1156031304;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Type);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FieldName);
            hashCode = hashCode * -1521134295 + Order.GetHashCode();
            return hashCode;
        }
    }
}