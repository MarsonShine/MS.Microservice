using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus.Tests
{
    public class IntegrationEventTests
    {
        [Fact]
        public void DefaultConstructor_SetsIdAndCreationDate()
        {
            var before = DateTime.UtcNow;
            var evt = new IntegrationEvent();
            var after = DateTime.UtcNow;

            Assert.NotEqual(Guid.Empty, evt.Id);
            Assert.InRange(evt.CreationDate, before, after);
        }

        [Fact]
        public void ParameterizedConstructor_SetsValues()
        {
            var id = Guid.NewGuid();
            var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var evt = new IntegrationEvent(id, date);

            Assert.Equal(id, evt.Id);
            Assert.Equal(date, evt.CreationDate);
        }

        [Fact]
        public void TwoInstances_HaveDifferentIds()
        {
            var evt1 = new IntegrationEvent();
            var evt2 = new IntegrationEvent();

            Assert.NotEqual(evt1.Id, evt2.Id);
        }
    }
}
