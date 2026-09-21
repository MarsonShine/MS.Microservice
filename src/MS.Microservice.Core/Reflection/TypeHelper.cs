using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Collections.Generic;

namespace MS.Microservice.Core.Reflection
{
    public class TypeHelper
    {
        public static T? GetDefaultValue<T>() => default;

        public static bool IsDefaultValue<T>([AllowNull] T value)
            => EqualityComparer<T>.Default.Equals(value, default);

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

        public static string GetFullMethodName<T>(string methodName)
        {
            ArgumentException.ThrowIfNullOrEmpty(methodName);
            return typeof(T).FullName + "." + methodName;
        }
    }
}
