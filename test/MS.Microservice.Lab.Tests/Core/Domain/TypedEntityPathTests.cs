using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Domain;

namespace MS.Microservice.Lab.Tests.Core.Domain;

public sealed class TypedEntityPathTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    public void NullableIdUsesCSharpDefaultThroughoutEntityMethods(int? id, bool transient)
    {
        var first = new Row<int?> { Id = id };
        var second = new Row<int?> { Id = id };
        Assert.Equal(transient, first.IsTransient());
        Assert.Equal(!transient, first.Equals(second));
        Assert.Equal(!transient, first.EntityEquals(second));
        Assert.Equal(!transient, first == second);
        Assert.Equal(transient, EntityHelper.HasDefaultKeys(first));
    }

    [Fact]
    public void WholeEntityEqualityPathDoesNotAllocateBoxes()
    {
        Check(7);
        Check(7L);
        Check(Guid.NewGuid());
        Check<int?>(0);
        Check(new Key(Guid.NewGuid(), 7));
    }

    private static void Check<TKey>(TKey id)
    {
        var first = new Row<TKey> { Id = id };
        var second = new Row<TKey> { Id = id };
        for (var i = 0; i < 1000; i++) Exercise(first, second);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var same = true;
        for (var i = 0; i < 10_000; i++) same &= Exercise(first, second);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.Equal(0, allocated);
    }

    private static bool Exercise<TKey>(Row<TKey> first, Row<TKey> second)
        => !first.IsTransient() && first.Equals(second) && first.EntityEquals(second) && first == second
            && first.GetHashCode() == second.GetHashCode();

    private sealed class Row<TKey> : EntityBase<TKey>;
    private readonly record struct Key(Guid Tenant, long Number);
}
