using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynHECS.DataTypes
{
    public struct ResolverData 
    {
        public string TypeToResolve;
        public StructDeclarationSyntax StructDeclaration;
    }
}