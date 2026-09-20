using MS.Microservice.Core.Extension;
using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.ValidatedShuffleExample;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.ValidatedShuffleExample;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ValidatedShuffleExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    public void CopyingExamples_PreserveTheSameInputAndMultiset(int count)
    {
        var source = Enumerable.Range(0, count).ToList();
        var legacy = LegacyExample.Shuffle(source);
        var current = StaticExample.Shuffle(source);

        Assert.Equal(Enumerable.Range(0, count), source);
        Assert.Equal(source, legacy.Order());
        Assert.Equal(source, current.Order());
        Assert.NotSame(source, legacy);
        Assert.NotSame(source, current);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    public void LegacyExample_ReproducesTheUnboundedSearchWithoutHanging(int count)
    {
        var items = RepeatedValues(count);

        Assert.Throws<ComparisonLimitException>(() => LegacyExample.ValidatedShuffle(items));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(128)]
    public void StaticExampleAndProduction_BothHandleTheLegacyFailure(int count)
    {
        var example = RepeatedValues(count);
        var production = RepeatedValues(count);

        StaticExample.ValidatedShuffle(example);
        production.ValidatedShuffle();

        Assert.Equal(count, example.Count);
        Assert.Equal(count, production.Count);
        Assert.All(example.Concat(production), item => Assert.Equal(1, item.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    public void StaticExampleAndProduction_SatisfyTheSameUniqueValueContract(int count)
    {
        var expected = Enumerable.Range(0, count).ToArray();
        var example = expected.ToList();
        var production = expected.ToList();

        StaticExample.ValidatedShuffle(example);
        production.ValidatedShuffle();

        foreach (var result in new[] { example, production })
        {
            Assert.Equal(expected, result.Order());
            if (count > 1)
                for (int i = 0; i < count; i++) Assert.NotEqual(expected[i], result[i]);
        }
    }

    private static List<ProbeValue> RepeatedValues(int count)
    {
        var budget = new ComparisonBudget(count * 16);
        return Enumerable.Range(0, count).Select(_ => new ProbeValue(1, budget)).ToList();
    }

    private sealed class ComparisonLimitException : Exception;

    private sealed class ComparisonBudget(int remaining)
    {
        public void Consume()
        {
            if (--remaining < 0) throw new ComparisonLimitException();
        }
    }

    private sealed class ProbeValue(int value, ComparisonBudget budget) : IEquatable<ProbeValue>
    {
        public int Value => value;
        public override int GetHashCode() => value;
        public override bool Equals(object? other) => other is ProbeValue item && Equals(item);

        public bool Equals(ProbeValue? other)
        {
            budget.Consume();
            return other is not null && value == other.Value;
        }
    }
}
