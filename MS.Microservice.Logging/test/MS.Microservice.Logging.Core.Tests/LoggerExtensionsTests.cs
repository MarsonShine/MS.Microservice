using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MS.Microservice.Logging.Core.Tests;

public sealed class LoggerExtensionsTests
{
    [Fact]
    public void LogHttpRequest_ShouldWriteExpectedFormattedMessage()
    {
        var logger = new TestLogger();

        logger.LogHttpRequest("GET", "/orders/42", 204, 87);

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].LogLevel.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Be("HTTP GET /orders/42 -> 204 in 87ms");
    }

    private sealed class TestLogger : ILogger
    {
        public List<TestLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new TestLogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record TestLogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}