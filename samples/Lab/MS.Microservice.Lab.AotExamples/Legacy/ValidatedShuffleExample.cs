// Preserved from master@7b1c78a; local static call replaces the equivalent extension call.
namespace MS.Microservice.Lab.AotExamples.Legacy;

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
		/// 带验证的洗牌算法，确保打破原有顺序
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
			// 如果多次尝试仍然失败，使用强制错位
			ApplyGuaranteedDerangement(list, originalOrder);
		}
	}

	private static void PerformAdvancedShuffle<T>(List<T> list)
	{
		var random = Random.Shared;
		for (int phase = 0; phase < 3; phase++)
		{
			// 阶段1：标准 Fisher-Yates
			Shuffle(list);

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
