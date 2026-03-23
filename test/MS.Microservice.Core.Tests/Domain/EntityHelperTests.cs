using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Domain;

namespace MS.Microservice.Core.Tests.Domain
{
    public class EntityHelperTests
    {
        // Concrete test entity
        private class TestEntity : Entity<int>
        {
            public TestEntity(int id) { Id = id; }
        }

        private class TestStringEntity : Entity<string>
        {
            public TestStringEntity(string id) { Id = id; }
        }

        private class OtherEntity : Entity<int>
        {
            public OtherEntity(int id) { Id = id; }
        }

        [Fact]
        public void EntityEquals_SameReference_ReturnsTrue()
        {
            var entity = new TestEntity(1);
            Assert.True(EntityHelper.EntityEquals(entity, entity));
        }

        [Fact]
        public void EntityEquals_NullEntity1_ReturnsFalse()
        {
            var entity = new TestEntity(1);
            Assert.False(EntityHelper.EntityEquals(null!, entity));
        }

        [Fact]
        public void EntityEquals_NullEntity2_ReturnsFalse()
        {
            var entity = new TestEntity(1);
            Assert.False(EntityHelper.EntityEquals(entity, null!));
        }

        [Fact]
        public void EntityEquals_DifferentUnrelatedTypes_ReturnsFalse()
        {
            var e1 = new TestEntity(1);
            var e2 = new OtherEntity(1);
            Assert.False(EntityHelper.EntityEquals(e1, e2));
        }

        [Fact]
        public void EntityEquals_BothDefaultKeys_ReturnsFalse()
        {
            var e1 = new TestEntity(0);
            var e2 = new TestEntity(0);
            Assert.False(EntityHelper.EntityEquals(e1, e2));
        }

        [Fact]
        public void EntityEquals_SameKeys_ReturnsTrue()
        {
            var e1 = new TestEntity(42);
            var e2 = new TestEntity(42);
            Assert.True(EntityHelper.EntityEquals(e1, e2));
        }

        [Fact]
        public void EntityEquals_DifferentKeys_ReturnsFalse()
        {
            var e1 = new TestEntity(1);
            var e2 = new TestEntity(2);
            Assert.False(EntityHelper.EntityEquals(e1, e2));
        }

        [Fact]
        public void HasDefaultKeys_DefaultIntKey_ReturnsTrue()
        {
            var entity = new TestEntity(0);
            Assert.True(EntityHelper.HasDefaultKeys(entity));
        }

        [Fact]
        public void HasDefaultKeys_NonDefaultIntKey_ReturnsFalse()
        {
            var entity = new TestEntity(5);
            Assert.False(EntityHelper.HasDefaultKeys(entity));
        }

        [Fact]
        public void HasDefaultKeys_NegativeIntKey_ReturnsTrue()
        {
            // Negative int is treated as default (EF Core workaround: <= 0)
            var entity = new TestEntity(-1);
            Assert.True(EntityHelper.HasDefaultKeys(entity));
        }

        [Fact]
        public void HasDefaultId_DefaultInt_ReturnsTrue()
        {
            var entity = new TestEntity(0);
            Assert.True(EntityHelper.HasDefaultId(entity));
        }

        [Fact]
        public void HasDefaultId_NonDefaultInt_ReturnsFalse()
        {
            var entity = new TestEntity(10);
            Assert.False(EntityHelper.HasDefaultId(entity));
        }

        [Fact]
        public void HasDefaultId_NegativeInt_ReturnsTrue()
        {
            var entity = new TestEntity(-5);
            Assert.True(EntityHelper.HasDefaultId(entity));
        }

        [Fact]
        public void HasDefaultId_DefaultString_ReturnsTrue()
        {
            var entity = new TestStringEntity(null!);
            Assert.True(EntityHelper.HasDefaultId(entity));
        }

        [Fact]
        public void HasDefaultId_NonDefaultString_ReturnsFalse()
        {
            var entity = new TestStringEntity("abc");
            Assert.False(EntityHelper.HasDefaultId(entity));
        }
    }
}
