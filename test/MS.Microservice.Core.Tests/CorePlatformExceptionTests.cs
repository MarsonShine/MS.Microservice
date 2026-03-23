using System;
using FluentAssertions;

namespace MS.Microservice.Core.Tests
{
    public class CorePlatformExceptionTests
    {
        [Fact]
        public void DefaultConstructor_CreatesException()
        {
            var ex = new CorePlatformException();
            ex.Message.Should().NotBeNull();
            ex.Code.Should().Be(0);
        }

        [Fact]
        public void Constructor_WithCodeAndMessage_SetsProperties()
        {
            var ex = new CorePlatformException(404, "Not found");
            ex.Code.Should().Be(404);
            ex.Message.Should().Be("Not found");
        }

        [Fact]
        public void Constructor_WithInnerException_SetsInnerException()
        {
            var inner = new InvalidOperationException("inner error");
            var ex = new CorePlatformException("outer error", inner);
            ex.Message.Should().Be("outer error");
            ex.InnerException.Should().BeSameAs(inner);
        }

        [Fact]
        public void IsException_InheritsFromException()
        {
            var ex = new CorePlatformException(500, "server error");
            ex.Should().BeAssignableTo<Exception>();
        }
    }
}
