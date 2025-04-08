using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MS.Microservice.Core.Extension
{
    public static partial class IEnumerableExtensions
    {
        public static ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary<TSource, TKey, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector) where TKey : class
        {
            return source.ToImmutableDictionary(
                keySelector,
                valueSelector,
                (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
        }

        public static ImmutableDictionary<TKey, TSource> ToImmutableReferenceDictionary<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector) where TKey : class
        {
            return source.ToImmutableDictionary(
                keySelector,
                (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
        }

        public static ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary<TKey, TValue>(
        this IDictionary<TKey, TValue> source) where TKey : class
        {
            return source.ToImmutableDictionary(
                ReferenceEqualityComparer.Instance);
        }

        public static ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary<TKey, TValue, TSourceValue>(
        this IDictionary<TKey, TSourceValue> source,
        Func<KeyValuePair<TKey, TSourceValue>, TValue> valueSelector) where TKey : class
        {
            return source.ToImmutableDictionary(
                pair => pair.Key,
                valueSelector,
                (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
        }

        public static ImmutableDictionary<string, TValue> ToImmutableOrdinalDictionary<TSource, TValue>(
            this IEnumerable<TSource> source,
            Func<TSource, string> keySelector,
            Func<TSource, TValue> valueSelector)
        {
            return source.ToImmutableDictionary(keySelector, valueSelector, StringComparer.Ordinal);
        }
    }
}
