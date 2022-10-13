using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;

namespace HECSFramework.Core.Generator
{
    public partial class CodeGenerator
    {
        public struct GatheredField
        {
            public string Type;
            public string FieldName;
            public int Order;
            public CSharpSyntaxNode Node;
            public string ResolverName;

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
}