using MS.Microservice.Core.Extension;

namespace MS.Microservice.Core.Tests.Extensions;

public sealed class ValidatedShuffleRegressionTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(128)]
    public void ValidatedShuffle_BoundsEqualityWorkForRepeatedValues(int count)
    {
        var budget = new ComparisonBudget(count * 16);
        var items = Enumerable.Range(0, count).Select(_ => new BoundedValue(1, budget)).ToList();

        items.ValidatedShuffle();

        Assert.Equal(count, items.Count);
        Assert.All(items, item => Assert.Equal(1, item.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(128)]
    public void ValidatedShuffle_PreservesUniqueValuesAndRemovesFixedPositions(int count)
    {
        var original = Enumerable.Range(0, count).ToArray();
        var items = original.ToList();

        items.ValidatedShuffle();

        Assert.Equal(original, items.Order());
        if (count > 1)
            Assert.All(items.Select((value, index) => (value, index)), pair => Assert.NotEqual(pair.index, pair.value));
    }

    [Fact]
    public void RepairFixedPositions_HandlesEveryUniquePermutationThroughSevenElements()
    {
        for (int count = 2; count <= 7; count++)
        {
            var original = Enumerable.Range(0, count).ToArray();
            foreach (var permutation in Permutations(original.ToArray(), 0))
            {
                var items = permutation.ToList();
                ListHelper.RepairFixedPositions(items, original);
                Assert.Equal(original, items.Order());
                for (int i = 0; i < count; i++) Assert.NotEqual(original[i], items[i]);
            }
        }
    }

    [Fact]
    public void RepairFixedPositions_WithDuplicateValuesPreservesMultisetAndAlreadyDisplacedPositions()
    {
        int[][] originals = [[1, 1], [1, 1, 2], [1, 1, 1, 2], [1, 1, 2, 2], [1, 2, 1, 3, 2]];
        foreach (var original in originals)
        foreach (var permutation in Permutations(original.ToArray(), 0))
        {
            var items = permutation.ToList();
            ListHelper.RepairFixedPositions(items, original);
            Assert.Equal(original.Order(), items.Order());
            for (int i = 0; i < original.Length; i++)
                if (permutation[i] != original[i]) Assert.NotEqual(original[i], items[i]);
        }
    }

    [Fact]
    public void ValidatedShuffle_PreservesDuplicateMultiplicityWithBoundedWork()
    {
        var budget = new ComparisonBudget(64 * 16);
        var items = Enumerable.Range(0, 64).Select(i => new BoundedValue(i % 8 == 0 ? 2 : 1, budget)).ToList();

        items.ValidatedShuffle();

        Assert.Equal(56, items.Count(item => item.Value == 1));
        Assert.Equal(8, items.Count(item => item.Value == 2));
    }

    [Fact]
    public void ValidatedShuffle_HandlesNullAlongsideUniqueReferenceValues()
    {
        List<string> items = [null!, "a", "b"];
        var original = items.ToArray();

        items.ValidatedShuffle();

        Assert.Equal(original.Order(), items.Order());
        for (int i = 0; i < items.Count; i++) Assert.NotEqual(original[i], items[i]);
    }

    [Fact]
    public void AdvancedShuffle_DoesNotAllocateListCopies()
    {
        var items = Enumerable.Range(0, 8192).ToList();
        for (int i = 0; i < 4; i++) ListHelper.PerformAdvancedShuffle(items);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++) ListHelper.PerformAdvancedShuffle(items);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Even one copied backing array costs 32 KiB; allow small runtime bookkeeping.
        Assert.True(allocated < 1024, $"In-place shuffle allocated {allocated} bytes.");
        Assert.Equal(Enumerable.Range(0, items.Count), items.Order());
    }

    [Fact]
    public void CopyingShuffle_LeavesItsInputUnchanged()
    {
        IList<int> original = new List<int> { 1, 2, 3, 4 };

        var result = original.Shuffle();

        Assert.NotSame(original, result);
        Assert.Equal(new[] { 1, 2, 3, 4 }, original);
        Assert.Equal(original, result.Order());
    }

    private static IEnumerable<int[]> Permutations(int[] values, int index)
    {
        if (index == values.Length)
        {
            yield return values.ToArray();
            yield break;
        }
        for (int i = index; i < values.Length; i++)
        {
            (values[index], values[i]) = (values[i], values[index]);
            foreach (var permutation in Permutations(values, index + 1)) yield return permutation;
            (values[index], values[i]) = (values[i], values[index]);
        }
    }

    private sealed class ComparisonBudget(int remaining)
    {
        public void Consume()
        {
            if (--remaining < 0)
                throw new InvalidOperationException("Shuffle exceeded its finite comparison budget.");
        }
    }

    private sealed class BoundedValue(int value, ComparisonBudget budget) : IEquatable<BoundedValue>
    {
        public int Value => value;

        public override bool Equals(object? other) => other is BoundedValue item && Equals(item);

        public override int GetHashCode() => value;

        public bool Equals(BoundedValue? other)
        {
            budget.Consume();
            return other is not null && value == other.Value;
        }
    }
}
