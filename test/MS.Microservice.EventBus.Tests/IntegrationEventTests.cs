using MS.Microservice.EventBus.Events;
using System.Text.Json;

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
            Assert.Equal(DateTimeKind.Utc, evt.CreationDate.Kind);
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

        [Fact]
        public void JsonConstructor_RoundTripsFromSerializedPayload()
        {
            var id = Guid.NewGuid();
            var creationDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var json = $$"""
            {"Id":"{{id}}","CreationDate":"{{creationDate:O}}"}
            """;

            var evt = JsonSerializer.Deserialize<IntegrationEvent>(json);

            Assert.NotNull(evt);
            Assert.Equal(id, evt.Id);
            Assert.Equal(creationDate, evt.CreationDate);
        }
    }
}
