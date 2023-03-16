using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MS.Microservice.Core.Extension
{
    public static class ICollectionExtensions
    {
        public static bool IsNullOrEmpty<T>([MaybeNull] this ICollection<T> source)
        {
            return source == null || source.Count <= 0;
        }

        public static bool AddIfNotContains<T>([NotNull] this ICollection<T> source, T item)
        {
            Check.NotNull(source, nameof(source));

            if (source.Contains(item))
            {
                return false;
            }

            source.Add(item);
            return true;
        }

        public static IEnumerable<T> AddIfNotContains<T>([NotNull] this ICollection<T> source, IEnumerable<T> items)
        {
            Check.NotNull(source, nameof(source));

            var addedItems = new List<T>();

            foreach (var item in items)
            {
                if (source.Contains(item))
                {
                    continue;
                }

                source.Add(item);
                addedItems.Add(item);
            }

            return addedItems;
        }

        public static IList<T> RemoveAll<T>([NotNull] this ICollection<T> source, Func<T, bool> predicate)
        {
            var items = source.Where(predicate).ToList();

            foreach (var item in items)
            {
                source.Remove(item);
            }

            return items;
        }

        public static bool ContainsAll<T>([NotNull] this ICollection<T> source, IEnumerable<T> items, IEqualityComparer<T> comparer)
        {
            Check.NotNull(source, nameof(source));
            foreach (T item in items)
            {
                if (!source.Contains(item, comparer))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
