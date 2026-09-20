using System.Collections;
using MS.Microservice.Core.Extension;
using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.ArrayConversionExample;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.ArrayConversionExample;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ArrayConversionExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public void ExamplesAndProduction_ProduceTheSameResultWithDifferentEnumerationCosts(int count)
    {
        var legacySource = new TrackedSource(count);
        var staticSource = new TrackedSource(count);
        var productionSource = new TrackedSource(count);

        var legacy = LegacyExample.ToArray(legacySource, static value => value * 2);
        var current = StaticExample.ToArray(staticSource, static value => value * 2);
        var production = productionSource.ToArray(static value => value * 2);

        Assert.Equal(Enumerable.Range(0, count).Select(value => value * 2), legacy);
        Assert.Equal(legacy, current);
        Assert.Equal(legacy, production);
        Assert.Equal(2, legacySource.Enumerations);
        Assert.Equal(count * 2, legacySource.Reads);
        Assert.Equal(1, staticSource.Enumerations);
        Assert.Equal(count, staticSource.Reads);
        Assert.Equal(1, productionSource.Enumerations);
        Assert.Equal(count, productionSource.Reads);
    }

    [Fact]
    public void SingleUseInput_ReproducesLegacyFailureAndSucceedsWithStaticImplementations()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LegacyExample.ToArray(new TrackedSource(3, singleUse: true), static value => value));

        Assert.Equal(new[] { 0, 1, 2 },
            StaticExample.ToArray(new TrackedSource(3, singleUse: true), static value => value));
        Assert.Equal(new[] { 0, 1, 2 },
            new TrackedSource(3, singleUse: true).ToArray(static value => value));
    }

    private sealed class TrackedSource(int count, bool singleUse = false) : IEnumerable<int>
    {
        public int Enumerations { get; private set; }
        public int Reads { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            if (++Enumerations > 1 && singleUse)
                throw new InvalidOperationException("Source cannot be enumerated twice.");
            return Read().GetEnumerator();
        }

        private IEnumerable<int> Read()
        {
            for (int i = 0; i < count; i++)
            {
                Reads++;
                yield return i;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
