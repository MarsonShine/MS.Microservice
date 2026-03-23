using MS.Microservice.Domain.Aggregates.LogAggregate;
using System;

namespace MS.Microservice.Core.Tests.Domain
{
    public class LogAggregateRootTests
    {
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var log = new LogAggregateRoot(
                eventName: "UserLogin",
                methodName: "Login",
                type: LogEventTypeEnum.Create,
                description: "User logged in",
                content: "{\"userId\":1}",
                creatorId: 42,
                ip: "192.168.1.1",
                telephone: "13800138000");

            Assert.Equal("UserLogin", log.EventName);
            Assert.Equal("Login", log.MethodName);
            Assert.Equal(LogEventTypeEnum.Create, log.Type);
            Assert.Equal("User logged in", log.Description);
            Assert.Equal("{\"userId\":1}", log.Content);
            Assert.Equal(42, log.CreatorId);
            Assert.Equal("192.168.1.1", log.IP);
            Assert.Equal("13800138000", log.Telephone);
            Assert.True(log.CreatedAt <= DateTime.Now);
            Assert.True(log.CreatedAt > DateTime.Now.AddSeconds(-5));
        }

        [Fact]
        public void Constructor_SetsCreatedAtToNow()
        {
            var before = DateTime.Now;
            var log = new LogAggregateRoot("evt", "method", LogEventTypeEnum.Activation, "desc", "content", 1, "127.0.0.1", "phone");
            var after = DateTime.Now;

            Assert.InRange(log.CreatedAt, before, after);
        }

        [Fact]
        public void Id_DefaultsToZero()
        {
            var log = new LogAggregateRoot("evt", "method", LogEventTypeEnum.Append, "desc", "content", 1, "127.0.0.1", "phone");
            Assert.Equal(0L, log.Id);
        }

        [Fact]
        public void IsTransient_WhenDefaultId_ReturnsTrue()
        {
            var log = new LogAggregateRoot("evt", "method", LogEventTypeEnum.Create, "desc", "content", 1, "127.0.0.1", "phone");
            Assert.True(log.IsTransient());
        }

        [Fact]
        public void IsTransient_WhenIdSet_ReturnsFalse()
        {
            var log = new LogAggregateRoot("evt", "method", LogEventTypeEnum.Create, "desc", "content", 1, "127.0.0.1", "phone");
            log.Id = 100L;
            Assert.False(log.IsTransient());
        }
    }
}
