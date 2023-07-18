using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace MS.Microservice.Core.Reflection
{
    public class TypeHelper
    {
        public static object? GetDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }

            return null;
        }

        public static bool IsDefaultValue([MaybeNull] object obj)
        {
            if (obj == null)
            {
                return true;
            }

            return obj.Equals(GetDefaultValue(obj.GetType()));
        }

        public static string GetGenericTypeName(Type type)
        {
            var typeName = string.Empty;

            if (type.IsGenericType)
            {
                var genericTypes = string.Join(",", type.GetGenericArguments().Select(t => t.Name).ToArray());
                typeName = $"{type.Name.Remove(type.Name.IndexOf('`'))}<{genericTypes}>";
            }
            else
            {
                typeName = type.Name;
            }

            return typeName;
        }

        public static string GetGenericTypeName(object obj) => GetGenericTypeName(obj.GetType());

        public static string GetFullMethodName(object obj, string methodName)
        {
            var typeInfo = obj.GetType().GetTypeInfo();
            return typeInfo.FullName + "." + typeInfo.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Name;
        }
    }
}
