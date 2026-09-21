using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Reflection;
using Xunit.Abstractions;

namespace MS.Microservice.Core.Tests.Reflection;

public sealed class TypedEntityKeyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(int.MinValue, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(int.MaxValue, false)]
    public void IntTemporaryKeysRemainTyped(int id, bool isTemporary)
    {
        var first = new Row<int>(id);
        var second = new Row<int>(id);
        Assert.Equal(isTemporary, EntityHelper.HasDefaultId(first));
        Assert.Equal(isTemporary, EntityHelper.HasDefaultKeys(first));
        Assert.Equal(!isTemporary, EntityHelper.EntityEquals(first, second));
        Assert.True(EntityHelper.EntityEquals(first, first));
    }

    [Theory]
    [InlineData(long.MinValue, true)]
    [InlineData(-1L, true)]
    [InlineData(0L, true)]
    [InlineData(long.MaxValue, false)]
    public void LongTemporaryKeysDoNotRequireConvert(long id, bool expected)
        => Assert.Equal(expected, EntityHelper.HasDefaultId(new Row<long>(id)));

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(7, false)]
    public void NullableKeyUsesItsDeclaredDefault(int? id, bool expected)
    {
        Assert.Equal(expected, TypeHelper.IsDefaultValue(id));
        Assert.Equal(expected, EntityHelper.HasDefaultKeys(new Row<int?>(id)));
        Assert.Equal(!expected, EntityHelper.EntityEquals(new Row<int?>(id), new Row<int?>(id)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("key", false)]
    public void ReferenceKeyKeepsNullAndEmptyDistinct(string? id, bool expected)
        => Assert.Equal(expected, EntityHelper.HasDefaultId(new Row<string?>(id)));

    [Fact]
    public void CompositeKeyUsesTypedValueEqualityWithoutRegistration()
    {
        var tenant = Guid.NewGuid();
        var first = new Row<CompositeKey>(new(tenant, 7));
        Assert.True(EntityHelper.EntityEquals(first, new Row<CompositeKey>(new(tenant, 7))));
        Assert.False(EntityHelper.EntityEquals(first, new Row<CompositeKey>(new(tenant, 8))));
        Assert.False(EntityHelper.EntityEquals(first, new Row<CompositeKey>(new(Guid.NewGuid(), 7))));
        Assert.True(EntityHelper.HasDefaultKeys(new Row<CompositeKey>(default)));
        // A composite value is compared to its own default, not to a boxed array of independent keys.
        Assert.False(EntityHelper.HasDefaultKeys(new Row<CompositeKey>(new(Guid.Empty, 7))));
        Assert.True(EntityHelper.EntityEquals(new Row<CompositeKey>(new(Guid.Empty, 7)), new Row<CompositeKey>(new(Guid.Empty, 7))));
    }

    [Fact]
    public void DefaultComparisonDoesNotExecuteStructConstructor()
    {
        Assert.Equal(99, new ConstructedKey().Value);
        Assert.Equal(0, TypeHelper.GetDefaultValue<ConstructedKey>().Value);
        Assert.True(EntityHelper.HasDefaultKeys(new Row<ConstructedKey>(default)));
        Assert.False(EntityHelper.HasDefaultKeys(new Row<ConstructedKey>(new())));
    }

    [Fact]
    public void EntityTypeNullAndIdentityRulesRemainDefined()
    {
        var row = new Row<int>(7);
        Assert.False(EntityHelper.EntityEquals<int>(null, null));
        Assert.False(EntityHelper.EntityEquals(null, row));
        Assert.False(EntityHelper.EntityEquals(row, null));
        Assert.False(EntityHelper.EntityEquals(row, new UnrelatedRow<int>(7)));
        Assert.True(EntityHelper.EntityEquals(row, new DerivedRow(7)));
        Assert.True(EntityHelper.EntityEquals(new DerivedRow(7), row));
        Assert.Throws<ArgumentNullException>(() => EntityHelper.HasDefaultId<int>(null!));
    }

    [Fact]
    public void WarmTypedPathsAllocateNoKeyArraysOrBoxes()
    {
        AssertNoAllocation(17);
        AssertNoAllocation(-17L);
        AssertNoAllocation(Guid.NewGuid());
        AssertNoAllocation<int?>(0);
        AssertNoAllocation<Status?>(Status.Active);
        AssertNoAllocation(new CompositeKey(Guid.NewGuid(), 17));
    }

    private void AssertNoAllocation<TKey>(TKey id)
    {
        var first = new Row<TKey>(id);
        var second = new Row<TKey>(id);
        for (var i = 0; i < 1000; i++) Evaluate(first, second);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 0; i < 10_000; i++) checksum += Evaluate(first, second);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10_000 * Evaluate(first, second), checksum);
        output.WriteLine($"{typeof(TKey)}: {allocated} bytes / 10,000 checks after warmup.");
        Assert.Equal(0, allocated);
    }

    private static int Evaluate<TKey>(Row<TKey> first, Row<TKey> second)
        => (TypeHelper.IsDefaultValue(first.Id) ? 1 : 0)
            + (EntityHelper.HasDefaultId(first) ? 2 : 0)
            + (EntityHelper.HasDefaultKeys(first) ? 4 : 0)
            + (EntityHelper.EntityEquals(first, second) ? 8 : 0);

    private class Row<TKey>(TKey id) : IEntity<TKey>
    {
        public TKey Id { get; set; } = id;
    }
    private sealed class UnrelatedRow<TKey>(TKey id) : IEntity<TKey>
    {
        public TKey Id { get; set; } = id;
    }
    private sealed class DerivedRow(int id) : Row<int>(id);
    private enum Status { None, Active }
    private readonly record struct CompositeKey(Guid Tenant, long Number);
    private readonly record struct ConstructedKey
    {
        public int Value { get; }
        public ConstructedKey() => Value = 99;
    }
}
