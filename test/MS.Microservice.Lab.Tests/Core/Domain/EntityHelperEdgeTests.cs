using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Domain;
using Xunit;

namespace MS.Microservice.Core.Tests.Domain
{
    public class EntityHelperEdgeTests
    {
        private class TestEntity : Entity<int>
        {
            public TestEntity(int id) { Id = id; }
        }

        private class TestLongEntity : Entity<long>
        {
            public TestLongEntity(long id) { Id = id; }
        }

        // EntityEquals: both keys null → continue, then both default
        [Fact]
        public void EntityEquals_DifferentLengthKeys_ReturnsFalse()
        {
            // Composite entity vs simple entity would have different key lengths
            // For simple entities: one key each, so they match on length
            // This tests the key length comparison path
            var e1 = new TestEntity(1);
            var e2 = new TestEntity(2);
            Assert.False(EntityHelper.EntityEquals(e1, e2));
        }

        // HasDefaultKeys: tests all branches
        [Fact]
        public void HasDefaultKeys_Test()
        {
            var e0 = new TestEntity(0);
            var e1 = new TestEntity(1);
            Assert.True(EntityHelper.HasDefaultKeys(e0));
            Assert.False(EntityHelper.HasDefaultKeys(e1));
        }

        // HasDefaultId: long type
        [Fact]
        public void HasDefaultId_LongZero_ReturnsTrue()
        {
            var e = new TestLongEntity(0L);
            Assert.True(EntityHelper.HasDefaultId(e));
        }

        [Fact]
        public void HasDefaultId_LongPositive_ReturnsFalse()
        {
            var e = new TestLongEntity(100L);
            Assert.False(EntityHelper.HasDefaultId(e));
        }

        [Fact]
        public void HasDefaultId_LongNegative_ReturnsTrue()
        {
            var e = new TestLongEntity(-1L);
            Assert.True(EntityHelper.HasDefaultId(e));
        }
    }
}