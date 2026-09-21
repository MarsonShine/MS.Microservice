using MS.Microservice.Lab.AotExamples.Legacy.EntityKeys;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests.EntityKeyHistory;

public sealed class DefaultValueComparisonTests
{
    [Fact]
    public void TypedDefault_DoesNotInvokeStructConstructor()
    {
        Assert.Equal(0, TypeHelper.GetDefaultValue<Key>().Value);
        Assert.Equal(99, new Key().Value);
        Assert.True(TypeHelper.IsDefaultValue(default(Key)));
        Assert.False(TypeHelper.IsDefaultValue(new Key()));
    }

    [Fact]
    public void BoxedCustomKeys_RequireExplicitClosedGenericRegistration()
    {
        Assert.Throws<NotSupportedException>(() => TypeHelper.IsDefaultBoxedValue(default(UnregisteredKey)));
        TypeHelper.RegisterDefaultValue<Key>();
        Assert.True(TypeHelper.IsDefaultBoxedValue(default(Key)));
        Assert.False(TypeHelper.IsDefaultBoxedValue(new Key()));
        TypeHelper.RegisterDefaultValue<Key>();
        Assert.True(EntityHelper.HasDefaultKeys(new Keys(default(Key))));
        Assert.True(EntityHelper.EntityEquals(new Keys(new Key()), new Keys(new Key())));
    }

    [Fact]
    public void NullableAndBoxedValues_KeepTheirDistinctDeclaredContracts()
    {
        Assert.True(TypeHelper.IsDefaultValue<int?>(null));
        Assert.False(TypeHelper.IsDefaultValue<int?>(0));
        Assert.True(TypeHelper.IsDefaultBoxedValue((int?)0));
        Assert.True(TypeHelper.IsDefaultBoxedValue(null));
        Assert.True(TypeHelper.IsDefaultBoxedValue(Guid.Empty));
        Assert.False(TypeHelper.IsDefaultBoxedValue(Guid.NewGuid()));
        Assert.False(TypeHelper.IsDefaultBoxedValue(""));
    }

    [Fact]
    public void TemporaryIdsAndCompositeKeys_PreserveEntityRules()
    {
        Assert.True(EntityHelper.HasDefaultKeys(new Keys(-1, 0L)));
        Assert.False(EntityHelper.EntityEquals(new Keys(-1), new Keys(-1)));
        Assert.True(EntityHelper.EntityEquals(new Keys(7, "part"), new Keys(7, "part")));
        Assert.False(EntityHelper.EntityEquals(new Keys(7, "part"), new Keys(8, "part")));
        Assert.False(EntityHelper.EntityEquals(new Keys(0, "part"), new Keys(0, "part")));
    }

    private readonly record struct Key
    {
        public int Value { get; }
        public Key() => Value = 99;
    }
    private readonly record struct UnregisteredKey(int Value);
    private sealed class Keys(params object[] values) : IEntity
    {
        public object[] GetKeys() => values;
    }
}
