using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MS.Microservice.Core.Extension
{
    public static partial class IEnumerableExtensions
    {
        extension<TSource, TKey, TValue>(IEnumerable<TSource> source) where TKey : class
        {
            public ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary(
                Func<TSource, TKey> keySelector,
                Func<TSource, TValue> valueSelector)
            {
                return source.ToImmutableDictionary(
                    keySelector,
                    valueSelector,
                    (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
            }
        }

        extension<TSource, TKey>(IEnumerable<TSource> source) where TKey : class
        {
            public ImmutableDictionary<TKey, TSource> ToImmutableReferenceDictionary(
                Func<TSource, TKey> keySelector)
            {
                return source.ToImmutableDictionary(
                    keySelector,
                    (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
            }
        }

        extension<TKey, TValue>(IDictionary<TKey, TValue> source) where TKey : class
        {
            public ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary()
            {
                return source.ToImmutableDictionary(
                    ReferenceEqualityComparer.Instance);
            }
        }

        extension<TKey, TValue, TSourceValue>(IDictionary<TKey, TSourceValue> source) where TKey : class
        {
            public ImmutableDictionary<TKey, TValue> ToImmutableReferenceDictionary(
                Func<KeyValuePair<TKey, TSourceValue>, TValue> valueSelector)
            {
                return source.ToImmutableDictionary(
                    pair => pair.Key,
                    valueSelector,
                    (IEqualityComparer<TKey>)ReferenceEqualityComparer.Instance);
            }
        }

        extension<TSource, TValue>(IEnumerable<TSource> source)
        {
            public ImmutableDictionary<string, TValue> ToImmutableOrdinalDictionary(
                Func<TSource, string> keySelector,
                Func<TSource, TValue> valueSelector)
            {
                return source.ToImmutableDictionary(keySelector, valueSelector, StringComparer.Ordinal);
            }
        }
    }
}
