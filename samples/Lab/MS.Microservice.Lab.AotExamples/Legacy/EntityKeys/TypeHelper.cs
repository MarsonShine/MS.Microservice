using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Collections.Generic;

namespace MS.Microservice.Lab.AotExamples.Legacy.EntityKeys
{
    public class TypeHelper
    {
        public static T? GetDefaultValue<T>() => default;

        public static bool IsDefaultValue<T>([AllowNull] T value)
            => EqualityComparer<T>.Default.Equals(value, default);

        /// <summary>为类型擦除的值类型注册编译期默认值比较；没有反射回退。</summary>
        public static void RegisterDefaultValue<T>() where T : struct
            => BoxedDefaults.Comparers.TryAdd(typeof(T), static value => IsDefaultValue((T)value));

        /// <summary>用于 object[] 实体键。可空值装箱后按实际值类型比较，保持原有键语义。</summary>
        public static bool IsDefaultBoxedValue(object? value)
        {
            if (value is null) return true;
            var type = value.GetType();
            if (!type.IsValueType) return false;
            if (BoxedDefaults.Comparers.TryGetValue(type, out var compare)) return compare(value);
            throw new NotSupportedException($"RegisterDefaultValue<T>() is required for boxed key type '{type.FullName}'.");
        }

        private static class BoxedDefaults
        {
            internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, bool>> Comparers = Create();

            private static System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, bool>> Create()
            {
                var values = new System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, bool>>();
                Add<bool>(); Add<char>(); Add<byte>(); Add<sbyte>(); Add<short>(); Add<ushort>();
                Add<int>(); Add<uint>(); Add<long>(); Add<ulong>(); Add<nint>(); Add<nuint>();
                Add<Half>(); Add<float>(); Add<double>(); Add<decimal>(); Add<Int128>(); Add<UInt128>();
                Add<Guid>(); Add<DateTime>(); Add<DateTimeOffset>(); Add<TimeSpan>(); Add<DateOnly>(); Add<TimeOnly>();
                return values;

                void Add<T>() where T : struct => values[typeof(T)] = static value => IsDefaultValue((T)value);
            }
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

        public static string GetFullMethodName<T>(string methodName)
        {
            ArgumentException.ThrowIfNullOrEmpty(methodName);
            return typeof(T).FullName + "." + methodName;
        }
    }
}
