using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Infrastructure.Util.Reflection
{
    public class TypeNameHelper
    {
        private const char DefaultNestedTypeDelimiter = '+';

        private static readonly Dictionary<Type, string> _builtInTypeNames = new Dictionary<Type, string>
        {
            { typeof(void), "void" },
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(long), "long" },
            { typeof(object), "object" },
            { typeof(sbyte), "sbyte" },
            { typeof(short), "short" },
            { typeof(string), "string" },
            { typeof(uint), "uint" },
            { typeof(ulong), "ulong" },
            { typeof(ushort), "ushort" }
        };
        public static string? GetTypeDisplayName(object item, bool fullName = true)
        {
            return item == null ? null : GetTypeDisplayName(item.GetType(), fullName);
        }
    }
}
