using System.Collections;
using MS.Microservice.Core.Extension;

namespace MS.Microservice.Core.Tests.Extensions;

public sealed class ArrayConversionRegressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    public void ToArray_EnumeratesSingleUseSourceOnce(int count)
    {
        var source = new SingleUseEnumerable<int>(Enumerable.Range(0, count));
        int conversions = 0;

        var result = source.ToArray(value => { conversions++; return value * 2; });

        Assert.Equal(Enumerable.Range(0, count).Select(value => value * 2), result);
        Assert.Equal(1, source.Enumerations);
        Assert.Equal(count, conversions);
    }

    [Fact]
    public void ToArray_EvaluatesDeferredValuesAndProjectionOnceInOrder()
    {
        var trace = new List<string>();
        IEnumerable<int> Source()
        {
            for (int i = 1; i <= 3; i++)
            {
                trace.Add($"read:{i}");
                yield return i;
            }
        }

        var result = Source().ToArray(value => { trace.Add($"convert:{value}"); return value * 10; });

        Assert.Equal(new[] { 10, 20, 30 }, result);
        Assert.Equal(new[] { "read:1", "convert:1", "read:2", "convert:2", "read:3", "convert:3" }, trace);
    }

    [Fact]
    public void ToArray_ConverterFailureStopsReadingAndDisposesEnumerator()
    {
        int reads = 0, disposals = 0;
        var failure = new InvalidOperationException("conversion failed");
        IEnumerable<int> Source()
        {
            try
            {
                for (int i = 1; i <= 3; i++)
                {
                    reads++;
                    yield return i;
                }
            }
            finally { disposals++; }
        }

        var actual = Assert.Throws<InvalidOperationException>(() =>
            Source().ToArray(value => value == 2 ? throw failure : value));

        Assert.Same(failure, actual);
        Assert.Equal(2, reads);
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void ToArray_SourceFailurePreservesCompletedConversionsAndDisposesEnumerator()
    {
        var converted = new List<int>();
        int disposals = 0;
        var failure = new InvalidOperationException("source failed");
        IEnumerable<int> Source()
        {
            try
            {
                yield return 1;
                yield return 2;
                throw failure;
            }
            finally { disposals++; }
        }

        var actual = Assert.Throws<InvalidOperationException>(() =>
            Source().ToArray(value => { converted.Add(value); return value; }));

        Assert.Same(failure, actual);
        Assert.Equal(new[] { 1, 2 }, converted);
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void ToArray_PreservesNullElementsAndCreatesAnIndependentArray()
    {
        string?[] source = ["a", null, "b"];

        var result = source.ToArray(static value => value);
        result[0] = "changed";

        Assert.Equal(new string?[] { "changed", null, "b" }, result);
        Assert.Equal(new string?[] { "a", null, "b" }, source);
    }

    [Fact]
    public void ToArray_RejectsNullArgumentsBeforeEnumerating()
    {
        IEnumerable<int> missing = null!;
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => missing.ToArray(static value => value)).ParamName);
        var source = new SingleUseEnumerable<int>([1]);

        Assert.Equal("conveter", Assert.Throws<ArgumentNullException>(() => source.ToArray<int, int>(null!)).ParamName);
        Assert.Equal(0, source.Enumerations);
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            if (++Enumerations > 1) throw new InvalidOperationException("Source cannot be enumerated twice.");
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
