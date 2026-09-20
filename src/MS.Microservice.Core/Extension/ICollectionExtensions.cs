using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace MS.Microservice.Core.Extension
{
	public static partial class ICollectionExtensions
	{
		extension<T>(ICollection<T>? source)
		{
			public bool IsNullOrEmpty()
			{
				return source == null || source.Count <= 0;
			}
		}

		extension<T>(ICollection<T> source)
		{
			public bool AddIfNotContains(T item)
			{
				Check.NotNull(source, nameof(source));

				if (source.Contains(item))
				{
					return false;
				}

				source.Add(item);
				return true;
			}

			public IEnumerable<T> AddIfNotContains(IEnumerable<T> items)
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

			public IList<T> RemoveAll(Func<T, bool> predicate)
			{
				var items = source.Where(predicate).ToList();

				foreach (var item in items)
				{
					source.Remove(item);
				}

				return items;
			}

			public bool ContainsAll(IEnumerable<T> items, IEqualityComparer<T> comparer)
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

		extension<T>(IList<T> source)
		{
			public IList<T> Shuffle()
			{
				Check.NotNull(source, nameof(source));
				return ListHelper.Shuffle(source);
			}
		}

		extension<TSource1, TSource2, TKey>(IEnumerable<TSource1> first)
		{
			public IEnumerable<TSource1> IntersectBy(
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
		}

		extension<TSource, TKey, TValue>(IEnumerable<TSource> source) where TKey : notnull
		{
			public Dictionary<TKey, TValue> ToDistinctDictionary(
				Func<TSource, TKey> keySelector,
				Func<TSource, TValue> valueSelector)
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
		}

		extension<T>(IEnumerable<T> target) where T : notnull
		{
			public IEnumerable<T> OrderByReference(IEnumerable<T> reference)
			{
				var indexMap = reference.Select((item, index) => new { item, index })
										.ToDictionary(x => x.item, x => x.index);

				return target.Where(indexMap.ContainsKey)
							 .OrderBy(item => indexMap[item])
							 .Concat(target.Where(item => !indexMap.ContainsKey(item)));
			}

			public List<T> SafeOrderByReference(IEnumerable<T> reference)
			{
				const int DICTIONARY_THRESHOLD = 100;

				var targetList = target.ToList();
				var referenceList = reference.ToList();

				if (targetList.Count <= DICTIONARY_THRESHOLD)
				{
					return SafeOrderByReferenceSmall(targetList, referenceList);
				}

				return SafeOrderByReferenceLarge(targetList, referenceList);
			}
		}

		extension<TA, TB, TKey>(IEnumerable<TB> target) where TKey : notnull
		{
			public IEnumerable<TB> OrderByReference(
				IEnumerable<TA> reference,
				Func<TA, TKey> referenceSelector,
				Func<TB, TKey> targetSelector)
			{
				var indexMap = reference.Select((item, index) => new { Key = referenceSelector(item), Index = index })
										.Where(x => x.Key != null)
										.ToDictionary(x => x.Key, x => x.Index);

				return target.Where(item => indexMap.ContainsKey(targetSelector(item)))
							 .OrderBy(item => indexMap[targetSelector(item)])
							 .Concat(target.Where(item => !indexMap.ContainsKey(targetSelector(item))));
			}

			public IEnumerable<TB> SafeOrderByReference(
				IEnumerable<TA> reference,
				Func<TA, TKey> referenceSelector,
				Func<TB, TKey> targetSelector)
			{
				const int QUEUE_THRESHOLD = 100;

				var targetList = target.ToList();
				var referenceList = reference.ToList();

				if (targetList.Count <= QUEUE_THRESHOLD)
				{
					return OrderByReferenceSmall(targetList, referenceList, referenceSelector, targetSelector);
				}

				return OrderByReferenceLarge(targetList, referenceList, referenceSelector, targetSelector);
			}
		}

		private static List<T> SafeOrderByReferenceSmall<T>(
			List<T> target,
			List<T> reference)
			where T : notnull
		{
			var used = new bool[target.Count];
			var result = new List<T>(target.Count);

			// 线性查找，适合小数据集
			foreach (var refItem in reference)
			{
				for (int i = 0; i < target.Count; i++)
				{
					if (!used[i] && EqualityComparer<T>.Default.Equals(target[i], refItem))
					{
						result.Add(target[i]);
						used[i] = true;
						break;
					}
				}
			}

			// 添加未匹配的项
			for (int i = 0; i < target.Count; i++)
			{
				if (!used[i])
				{
					result.Add(target[i]);
				}
			}

			return result;
		}

		private static List<T> SafeOrderByReferenceLarge<T>(
			List<T> target,
			List<T> reference)
			where T : notnull
		{
			var targetQueues = target.GroupBy(x => x)
									.ToDictionary(g => g.Key, g => new Queue<T>(g));

			var result = new List<T>(target.Count);

			foreach (var refItem in reference)
			{
				if (targetQueues.TryGetValue(refItem, out var queue) && queue.Count > 0)
				{
					result.Add(queue.Dequeue());
				}
			}

			// 添加剩余未匹配的项
			foreach (var queue in targetQueues.Values)
			{
				while (queue.Count > 0)
				{
					result.Add(queue.Dequeue());
				}
			}

			return result;
		}

		private static List<TB> OrderByReferenceSmall<TB, TA, TKey>(List<TB> target, List<TA> reference, Func<TA, TKey> referenceSelector, Func<TB, TKey> targetSelector) where TKey : notnull
		{
			var used = new bool[target.Count];
			var result = new List<TB>(target.Count);

			// 线性扫描，内存友好
			foreach (var refItem in reference)
			{
				var refKey = referenceSelector(refItem);
				if (refKey == null) continue;

				for (int i = 0; i < target.Count; i++)
				{
					if (!used[i] && EqualityComparer<TKey>.Default.Equals(targetSelector(target[i]), refKey))
					{
						result.Add(target[i]);
						used[i] = true;
						break;
					}
				}
			}

			// 添加未使用的项
			for (int i = 0; i < target.Count; i++)
			{
				if (!used[i])
				{
					result.Add(target[i]);
				}
			}

			return result;
		}

		// 大数据集的队列实现
		private static List<TB> OrderByReferenceLarge<TA, TB, TKey>(
			List<TB> target,
			List<TA> reference,
			Func<TA, TKey> referenceSelector,
			Func<TB, TKey> targetSelector)
			where TKey : notnull
		{
			var targetQueues = target.GroupBy(targetSelector)
									.ToDictionary(g => g.Key, g => new Queue<TB>(g));

			var result = new List<TB>(target.Count);

			foreach (var refItem in reference)
			{
				var refKey = referenceSelector(refItem);
				if (refKey != null &&
					targetQueues.TryGetValue(refKey, out var queue) &&
					queue.Count > 0)
				{
					result.Add(queue.Dequeue());
				}
			}

			foreach (var queue in targetQueues.Values)
			{
				while (queue.Count > 0)
				{
					result.Add(queue.Dequeue());
				}
			}

			return result;
		}
	}

	public static partial class ListHelper
	{
		public static List<T> Shuffle<T>(IList<T> source)
		{
			var random = Random.Shared;
			var result = new List<T>(source);
			for (int i = 0; i < result.Count; i++)
			{
				int j = random.Next(i, result.Count);
				if (j != i)
				{
					(result[j], result[i]) = (result[i], result[j]);
				}
			}
			return result;
		}

		extension<T>(List<T> list) where T : IEquatable<T>
		{
			/// <summary>
			/// 原地洗牌；元素数量至少为二且值互不相等时保证全部错位，重复值尽力错位且始终有限结束。
			/// </summary>
			public void ValidatedShuffle()
			{
				if (list.Count <= 1) return;

				var originalOrder = list.ToList();
				int maxAttempts = 10;
				int attempt = 0;

				do
				{
					attempt++;
					PerformAdvancedShuffle(list);
				}
				while (HasSignificantOrderPreservation(originalOrder, list) && attempt < maxAttempts);
				RepairFixedPositions(list, originalOrder);
			}
		}

		internal static void PerformAdvancedShuffle<T>(List<T> list)
		{
			var random = Random.Shared;
			for (int phase = 0; phase < 3; phase++)
			{
				// 阶段1：官方原地洗牌；Span 使用期间不增删元素。
				random.Shuffle(CollectionsMarshal.AsSpan(list));

				// 阶段2：随机交换
				int swaps = list.Count * 2;
				for (int i = 0; i < swaps; i++)
				{
					int a = random.Next(list.Count);
					int b = random.Next(list.Count);
					(list[a], list[b]) = (list[b], list[a]);
				}
			}
		}

		/// <summary>
		/// 检查是否还有显著的顺序保持
		/// </summary>
		private static bool HasSignificantOrderPreservation<T>(List<T> original, List<T> shuffled) where T : IEquatable<T>
		{
			int preservedCount = 0;
			int consecutivePreserved = 0;
			int maxConsecutive = 0;

			for (int i = 0; i < Math.Min(original.Count, shuffled.Count); i++)
			{
				if (EqualityComparer<T>.Default.Equals(original[i], shuffled[i]))
				{
					preservedCount++;
					consecutivePreserved++;
					maxConsecutive = Math.Max(maxConsecutive, consecutivePreserved);
				}
				else
				{
					consecutivePreserved = 0;
				}
			}

			// 如果保持原位置的元素超过10%，或连续3个以上元素保持顺序，认为洗牌不充分
			double preservationRate = (double)preservedCount / original.Count;
			return preservationRate > 0.1 || maxConsecutive > 2;
		}

		/// <summary>
		/// 轮换固定位置；重复值不一定能完全错位，不通过随机重试寻找不存在的候选。
		/// </summary>
		internal static void RepairFixedPositions<T>(List<T> list, IReadOnlyList<T> originalOrder)
		{
			var comparer = EqualityComparer<T>.Default;
			int firstFixed = -1;
			int previousFixed = -1;
			for (int i = 0; i < list.Count; i++)
			{
				if (!comparer.Equals(list[i], originalOrder[i])) continue;
				if (previousFixed >= 0)
				{
					(list[previousFixed], list[i]) = (list[i], list[previousFixed]);
				}
				else
				{
					firstFixed = i;
				}
				previousFixed = i;
			}

			if (firstFixed < 0 || firstFixed != previousFixed) return;
			for (int i = 0; i < list.Count; i++)
			{
				if (i != firstFixed &&
					!comparer.Equals(list[i], originalOrder[firstFixed]) &&
					!comparer.Equals(list[firstFixed], originalOrder[i]))
				{
					(list[firstFixed], list[i]) = (list[i], list[firstFixed]);
					return;
				}
			}
		}
	}
}
