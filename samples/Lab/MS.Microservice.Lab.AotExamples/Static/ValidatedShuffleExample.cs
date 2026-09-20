using System.Runtime.InteropServices;

namespace MS.Microservice.Lab.AotExamples.Static;

public static class ValidatedShuffleExample
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
