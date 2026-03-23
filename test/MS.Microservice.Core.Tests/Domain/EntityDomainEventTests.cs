using MS.Microservice.Domain;
using System.Linq;

namespace MS.Microservice.Core.Tests.Domain
{
    public class EntityDomainEventTests
    {
        private class TestDomainEvent : IDomainEvent
        {
            public string Message { get; }
            public TestDomainEvent(string message) { Message = message; }
        }

        private class ConcreteEntity : Entity<int>
        {
            public ConcreteEntity(int id) { Id = id; }
        }

        [Fact]
        public void AddDomainEvent_EventIsAdded()
        {
            var entity = new ConcreteEntity(1);
            var evt = new TestDomainEvent("created");

            entity.AddDomainEvent(evt);

            Assert.Single(entity.DomainEvents);
            Assert.Same(evt, entity.DomainEvents.First());
        }

        [Fact]
        public void AddDomainEvent_MultipleDomainEvents()
        {
            var entity = new ConcreteEntity(1);
            entity.AddDomainEvent(new TestDomainEvent("e1"));
            entity.AddDomainEvent(new TestDomainEvent("e2"));

            Assert.Equal(2, entity.DomainEvents.Count);
        }

        [Fact]
        public void RemoveDomainEvent_RemovesSpecificEvent()
        {
            var entity = new ConcreteEntity(1);
            var evt1 = new TestDomainEvent("e1");
            var evt2 = new TestDomainEvent("e2");
            entity.AddDomainEvent(evt1);
            entity.AddDomainEvent(evt2);

            entity.RemoveDomainEvent(evt1);

            Assert.Single(entity.DomainEvents);
            Assert.Same(evt2, entity.DomainEvents.First());
        }

        [Fact]
        public void ClearDomainEvents_RemovesAll()
        {
            var entity = new ConcreteEntity(1);
            entity.AddDomainEvent(new TestDomainEvent("e1"));
            entity.AddDomainEvent(new TestDomainEvent("e2"));

            entity.ClearDomainEvents();

            Assert.Empty(entity.DomainEvents);
        }

        [Fact]
        public void DomainEvents_NullByDefault_BeforeFirstAdd()
        {
            var entity = new ConcreteEntity(1);
            // DomainEvents returns null if no events have been added
            // The property returns _domainEvents?.AsReadOnly()!
            // Before first AddDomainEvent, _domainEvents is null
            var events = entity.DomainEvents;
            Assert.Null(events);
        }

        [Fact]
        public void GetKeys_ReturnsIdArray()
        {
            var entity = new ConcreteEntity(42);
            var keys = entity.GetKeys();
            Assert.Single(keys);
            Assert.Equal(42, keys[0]);
        }

        [Fact]
        public void EntityEquals_SameId_ReturnsTrue()
        {
            var e1 = new ConcreteEntity(5);
            var e2 = new ConcreteEntity(5);
            Assert.True(e1.EntityEquals(e2));
        }

        [Fact]
        public void EntityEquals_DifferentId_ReturnsFalse()
        {
            var e1 = new ConcreteEntity(1);
            var e2 = new ConcreteEntity(2);
            Assert.False(e1.EntityEquals(e2));
        }
    }
}
