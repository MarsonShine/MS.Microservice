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

		public static IList<T> Shuffle<T>([NotNull] this IList<T> source)
		{
			Check.NotNull(source, nameof(source));
			return ListHelper.Shuffle(source);
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

        public static List<T> SafeOrderByReference<T>(
            this IEnumerable<T> target,
            IEnumerable<T> reference)
            where T : notnull
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

        public static IEnumerable<TB> SafeOrderByReference<TA, TB, TKey>(
            this IEnumerable<TB> target,
            IEnumerable<TA> reference,
            Func<TA, TKey> referenceSelector,
            Func<TB, TKey> targetSelector)
            where TKey : notnull
        {
            const int QUEUE_THRESHOLD = 100; // 阈值可以根据实际测试调整

            var targetList = target.ToList();
            var referenceList = reference.ToList();

            // 小数据集：使用简单的线性查找（减少内存分配）
            if (targetList.Count <= QUEUE_THRESHOLD)
            {
                return OrderByReferenceSmall(targetList, referenceList, referenceSelector, targetSelector);
            }

            // 大数据集：使用队列优化
            return OrderByReferenceLarge(targetList, referenceList, referenceSelector, targetSelector);
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

	public static class ListHelper
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

        /// <summary>
        /// 带验证的洗牌算法，确保打破原有顺序
        /// </summary>
        public static void ValidatedShuffle<T>(this List<T> list) where T : IEquatable<T>
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
            // 如果多次尝试仍然失败，使用强制错位
            ApplyGuaranteedDerangement(list, originalOrder);
        }

        private static void PerformAdvancedShuffle<T>(List<T> list)
        {
            // 创建每次都不同的随机种子
            var random = Random.Shared;
            // 多阶段洗牌
            for (int phase = 0; phase < 3; phase++)
            {
                // 阶段1：标准 Fisher-Yates
                list.Shuffle();

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
                if (original[i].Equals(shuffled[i]))
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
        /// 强制错位，确保没有元素在原位置
        /// </summary>
        private static void ApplyGuaranteedDerangement<T>(List<T> list, List<T> originalOrder)
        {
            var random = Random.Shared;

            for (int i = 0; i < list.Count; i++)
            {
                // 如果当前位置的元素与原位置相同，找一个不同的位置交换
                if (list[i]!.Equals(originalOrder[i]))
                {
                    // 找到一个不是原位置的位置进行交换
                    int swapIndex;
                    do
                    {
                        swapIndex = random.Next(list.Count);
                    } while (swapIndex == i ||
                             (list[swapIndex]!.Equals(originalOrder[swapIndex]) && swapIndex != i));

                    (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
                }
            }
        }
    }
}
