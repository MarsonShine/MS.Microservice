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

		public static IEnumerable<TSource1> IntersectBy<TSource1, TSource2, TKey>(
			this IEnumerable<TSource1> first,
			IEnumerable<TSource2> second,
			Func<TSource1, TKey> firstKeySelector,
			Func<TSource2, TKey> secondKeySelector)
		{
			HashSet<TKey> keys = new(second.Select(secondKeySelector));
			foreach (var element in first)
			{
				if (keys.Contains(firstKeySelector(element)))
				{
					yield return element;
				}
			}
		}

		public static Dictionary<TKey, TValue> ToDistinctDictionary<TSource, TKey, TValue>(
			this IEnumerable<TSource> source,
			Func<TSource, TKey> keySelector,
			Func<TSource, TValue> valueSelector)
			where TKey : notnull
		{
			Dictionary<TKey, TValue> dictionary = new();
			foreach (var element in source)
			{
				TKey key = keySelector(element);
				if (!dictionary.ContainsKey(key))
				{
					dictionary.Add(key, valueSelector(element));
				}
			}
			return dictionary;
		}

		public static IEnumerable<T> OrderByReference<T>(
			this IEnumerable<T> target,
			IEnumerable<T> reference)
			where T : notnull
		{
			var indexMap = reference.Select((item, index) => new { item, index })
									.ToDictionary(x => x.item, x => x.index);

			return target.Where(indexMap.ContainsKey)
						 .OrderBy(item => indexMap[item])
						 .Concat(target.Where(item => !indexMap.ContainsKey(item)));
		}

		public static IEnumerable<TB> OrderByReference<TA, TB, TKey>(
			this IEnumerable<TB> target,
			IEnumerable<TA> reference,
			Func<TA, TKey> referenceSelector,
			Func<TB, TKey> targetSelector)
			where TKey : notnull
		{
			var indexMap = reference.Select((item, index) => new { Key = referenceSelector(item), Index = index })
									.Where(x => x.Key != null)
									.ToDictionary(x => x.Key, x => x.Index);

			return target.Where(item => indexMap.ContainsKey(targetSelector(item)))
						 .OrderBy(item => indexMap[targetSelector(item)])
						 .Concat(target.Where(item => !indexMap.ContainsKey(targetSelector(item))));
		}
	}
}
