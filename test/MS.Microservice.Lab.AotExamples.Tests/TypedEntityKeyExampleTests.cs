using CurrentApi = MS.Microservice.Lab.AotExamples.Static.EntityKeys.EntityHelper;
using LegacyApi = MS.Microservice.Lab.AotExamples.Legacy.EntityKeys.EntityHelper;
using Xunit;
using Xunit.Abstractions;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class TypedEntityKeyExampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(7, true)]
    public void OrdinaryIntKeyResultsAgreeAcrossTheActualImplementations(int id, bool equal)
    {
        var first = new Row<int>(id);
        var second = new Row<int>(id);
        Assert.Equal(equal, LegacyApi.EntityEquals(first, second));
        Assert.Equal(equal, CurrentApi.EntityEquals(first, second));
    }

    [Fact]
    public void NullableZeroDeliberatelyKeepsItsDeclaredType()
    {
        var first = new Row<int?>(0);
        var second = new Row<int?>(0);
        Assert.True(LegacyApi.HasDefaultKeys(first));
        Assert.False(CurrentApi.HasDefaultKeys(first));
        Assert.False(LegacyApi.EntityEquals(first, second));
        Assert.True(CurrentApi.EntityEquals(first, second));
    }

    [Fact]
    public void RemovingTheObjectArrayContractRemovesItsAllocation()
    {
        var first = new Row<int>(7);
        var second = new Row<int>(7);
        for (var i = 0; i < 1000; i++)
        {
            LegacyApi.EntityEquals(first, second);
            CurrentApi.EntityEquals(first, second);
        }
        int count = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) if (LegacyApi.EntityEquals(first, second)) count++;
        var legacyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) if (CurrentApi.EntityEquals(first, second)) count++;
        var currentBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(20_000, count);
        Assert.True(legacyBytes > 0);
        Assert.Equal(0, currentBytes);
        output.WriteLine($"10,000 warmed comparisons: legacy={legacyBytes} bytes, typed={currentBytes} bytes.");
    }

    private sealed class Row<TKey>(TKey id) : MS.Microservice.Core.Domain.Entity.IEntity<TKey>,
        MS.Microservice.Lab.AotExamples.Legacy.EntityKeys.IEntity<TKey>
    {
        public TKey Id { get; set; } = id;
        public object[] GetKeys() => [Id!];
    }
}
