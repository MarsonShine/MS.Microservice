using MS.Microservice.Domain;

namespace MS.Microservice.Core.Tests.Domain
{
    public class EntityBaseTests
    {
        private class TestEntityBase : EntityBase<int>
        {
            public TestEntityBase(int id) { Id = id; }
        }

        private class TestEntityBaseLong : EntityBase<long>
        {
            public TestEntityBaseLong(long id) { Id = id; }
        }

        [Fact]
        public void IsTransient_DefaultId_ReturnsTrue()
        {
            var entity = new TestEntityBase(0);
            Assert.True(entity.IsTransient());
        }

        [Fact]
        public void IsTransient_NonDefaultId_ReturnsFalse()
        {
            var entity = new TestEntityBase(5);
            Assert.False(entity.IsTransient());
        }

        [Fact]
        public void Equals_SameReference_ReturnsTrue()
        {
            var entity = new TestEntityBase(1);
            Assert.True(entity.Equals(entity));
        }

        [Fact]
        public void Equals_Null_ReturnsFalse()
        {
            var entity = new TestEntityBase(1);
            Assert.False(entity.Equals(null));
        }

        [Fact]
        public void Equals_SameIdSameType_ReturnsTrue()
        {
            var e1 = new TestEntityBase(10);
            var e2 = new TestEntityBase(10);
            Assert.True(e1.Equals(e2));
        }

        [Fact]
        public void Equals_DifferentId_ReturnsFalse()
        {
            var e1 = new TestEntityBase(1);
            var e2 = new TestEntityBase(2);
            Assert.False(e1.Equals(e2));
        }

        [Fact]
        public void Equals_BothTransient_ReturnsFalse()
        {
            var e1 = new TestEntityBase(0);
            var e2 = new TestEntityBase(0);
            Assert.False(e1.Equals(e2));
        }

        [Fact]
        public void Equals_OneTransient_ReturnsFalse()
        {
            var e1 = new TestEntityBase(0);
            var e2 = new TestEntityBase(5);
            Assert.False(e1.Equals(e2));
        }

        [Fact]
        public void GetHashCode_SameId_SameHash()
        {
            var e1 = new TestEntityBase(10);
            var e2 = new TestEntityBase(10);
            Assert.Equal(e1.GetHashCode(), e2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_Transient_UsesBaseHashCode()
        {
            var entity = new TestEntityBase(0);
            // Transient uses base.GetHashCode() which is based on object reference
            // Just ensure it doesn't throw
            var hash = entity.GetHashCode();
            Assert.IsType<int>(hash);
        }

        [Fact]
        public void OperatorEquals_BothNull_ReturnsTrue()
        {
            TestEntityBase? left = null;
            TestEntityBase? right = null;
            Assert.True(left == right);
        }

        [Fact]
        public void OperatorEquals_LeftNull_ReturnsFalse()
        {
            TestEntityBase? left = null;
            var right = new TestEntityBase(1);
            Assert.False(left == right);
        }

        [Fact]
        public void OperatorEquals_RightNull_ReturnsFalse()
        {
            var left = new TestEntityBase(1);
            TestEntityBase? right = null;
            Assert.False(left == right);
        }

        [Fact]
        public void OperatorEquals_SameId_ReturnsTrue()
        {
            var left = new TestEntityBase(10);
            var right = new TestEntityBase(10);
            Assert.True(left == right);
        }

        [Fact]
        public void OperatorNotEquals_DifferentId_ReturnsTrue()
        {
            var left = new TestEntityBase(1);
            var right = new TestEntityBase(2);
            Assert.True(left != right);
        }

        [Fact]
        public void OperatorNotEquals_SameId_ReturnsFalse()
        {
            var left = new TestEntityBase(10);
            var right = new TestEntityBase(10);
            Assert.False(left != right);
        }
    }
}
