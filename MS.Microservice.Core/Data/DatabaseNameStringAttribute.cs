using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MS.Microservice.Core.Data
{
    public class DatabaseNameStringAttribute : Attribute
    {
        [NotNull]
        public string Name { get; }

        public DatabaseNameStringAttribute([NotNull] string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            Name = name;
        }

        public static string GetConnStringName<T>()
        {
            return GetConnStringName(typeof(T));
        }

        public static string GetConnStringName(Type type)
        {
            var nameAttribute = type.GetTypeInfo().GetCustomAttribute<DatabaseNameStringAttribute>();

            if (nameAttribute == null)
            {
                return type.FullName;
            }

            return nameAttribute.Name;
        }
    }
}
