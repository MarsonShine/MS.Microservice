using MS.Microservice.Lab.AotExamples.Legacy.EntityKeys;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests.EntityKeyHistory
{
    // ========== EntityHelper ==========
    // Test entity implementations
    public class EntityHelperTestEntity : IEntity
    {
        private readonly object[] _keys;
        public EntityHelperTestEntity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EntityHelperIntEntity : IEntity<int>
    {
        public int Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EntityHelperIntEntity(int id) => Id = id;
    }

    public class EntityHelperLongEntity : IEntity<long>
    {
        public long Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EntityHelperLongEntity(long id) => Id = id;
    }

    public class EntityHelperOtherEntity : IEntity
    {
        private readonly object[] _keys;
        public EntityHelperOtherEntity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EntityHelperTests
    {
        [Fact] public void EntityEquals_BothNull() { Assert.False(EntityHelper.EntityEquals(null!, null!)); }
        [Fact] public void EntityEquals_OneNull() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), null!)); Assert.False(EntityHelper.EntityEquals(null!, new EntityHelperTestEntity(1))); }
        [Fact] public void EntityEquals_SameReference() { var e = new EntityHelperTestEntity(1); Assert.True(EntityHelper.EntityEquals(e, e)); }
        [Fact] public void EntityEquals_DifferentTypes() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperOtherEntity(1))); }
        [Fact] public void EntityEquals_DefaultKeys() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(0), new EntityHelperTestEntity(0))); }
        [Fact] public void EntityEquals_KeyLengthMismatch() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(1, 2))); }
        [Fact] public void EntityEquals_KeysEqual() { Assert.True(EntityHelper.EntityEquals(new EntityHelperTestEntity(1, "a"), new EntityHelperTestEntity(1, "a"))); }
        [Fact] public void EntityEquals_KeysNotEqual() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(2))); }
        [Fact] public void EntityEquals_NullKey() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(new object[] { null! }))); }

        [Fact] public void HasDefaultKeys_True() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(0))); }
        [Fact] public void HasDefaultKeys_NullKey() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(new object[] { null! }))); }
        [Fact] public void HasDefaultKeys_False() { Assert.False(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(1))); }
        [Fact] public void HasDefaultKeys_Long() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(0L))); }

        [Fact] public void HasDefaultId_True() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperIntEntity(0))); }
        [Fact] public void HasDefaultId_False() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperIntEntity(-1))); }
        [Fact] public void HasDefaultId_Long() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperLongEntity(0))); Assert.False(EntityHelper.HasDefaultId(new EntityHelperLongEntity(1))); }
    }

    public class EntityHelperMoreTests
    {
        [Fact] public void EntityEquals_Entity1MoreKeys() { Assert.False(EntityHelper.EntityEquals(new EH_Entity(1, 2), new EH_Entity(1))); }
        [Fact] public void EntityEquals_Entity1NullKey() { Assert.False(EntityHelper.EntityEquals(new EH_Entity(new object[] { null! }), new EH_Entity(1))); }
        [Fact] public void HasDefaultKeys_MultipleKeys_AllDefault() { Assert.True(EntityHelper.HasDefaultKeys(new EH_Entity(0, 0L))); }
        [Fact] public void HasDefaultKeys_MultipleKeys_OneNotDefault() { Assert.False(EntityHelper.HasDefaultKeys(new EH_Entity(1, 0L))); }
        [Fact] public void HasDefaultId_NegativeLong() { Assert.True(EntityHelper.HasDefaultId(new EH_LongEntity(-1L))); }
        [Fact] public void HasDefaultId_IntMaxCheck() { Assert.True(EntityHelper.HasDefaultId(new EH_LongEntity(0L))); }
    }

    public class EH_Entity : IEntity
    {
        private readonly object[] _keys;
        public EH_Entity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EH_LongEntity : IEntity<long>
    {
        public long Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EH_LongEntity(long id) => Id = id;
    }

}
