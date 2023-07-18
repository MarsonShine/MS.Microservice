using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Core.Extension
{
    public static class IEnumerableExtensions
    {
        public static int FindIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var index = 0;
            foreach (var item in source)
            {
                if (predicate(item)) return index;
                index++;
            }
            return -1;
        }

        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> doAction)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (doAction == null) throw new ArgumentNullException(nameof(doAction));
            foreach (TSource item in source)
            {
                doAction(item);
            }
        }

        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource, int> doAction)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (doAction == null) throw new ArgumentNullException(nameof(doAction));
            ForEachInterator(source, doAction);
        }

        private static void ForEachInterator<TSource>(IEnumerable<TSource> source, Action<TSource, int> dpActoin)
        {
            var index = -1;
            foreach (TSource item in source)
            {
                index = checked(index + 1);
                dpActoin(item, index);
            }
        }

        public static TResult[] ToArray<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> conveter)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (conveter == null) throw new ArgumentNullException(nameof(conveter));

            return ConvertInterator(source, conveter);
        }

        private static TResult[] ConvertInterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> conveter)
        {
            var sourceArray = source.ToArray();
            TResult[] results = new TResult[sourceArray.Length];

            ForEachInterator(source, (item, index) =>
            {
                results[index] = conveter(item);
            });

            return results;
        }

        public static string JoinAsString<TSource>(this IEnumerable<TSource>? source, string separator)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return string.Join(separator, source);
        }
    }
}
