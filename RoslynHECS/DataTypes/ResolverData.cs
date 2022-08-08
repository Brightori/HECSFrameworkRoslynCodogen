using System;

namespace RoslynHECS.DataTypes
{
    public struct ResolverData 
    {
        public string TypeToResolve;
        public string ResolverName;

        public override bool Equals(object obj)
        {
            return obj is ResolverData data &&
                   TypeToResolve == data.TypeToResolve &&
                   ResolverName == data.ResolverName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TypeToResolve, ResolverName);
        }
    }
}