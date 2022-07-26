using System;

namespace RoslynHECS.DataTypes
{
    public class ShortIDObject
    {
        public string Type;
        public ushort ShortId;
        public int TypeCode;
        public byte DataType;

        public override bool Equals(object obj)
        {
            return obj is ShortIDObject @object &&
                   Type == @object.Type &&
                   ShortId == @object.ShortId &&
                   TypeCode == @object.TypeCode &&
                   DataType == @object.DataType;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, ShortId, TypeCode, DataType);
        }
    }
}