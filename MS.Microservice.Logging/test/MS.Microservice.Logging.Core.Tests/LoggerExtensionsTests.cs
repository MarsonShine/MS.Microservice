using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void LogOverloads_ShouldWriteExpectedMessagesAndLevels()
    {
        var logger = new TestLogger();
        var exception = new InvalidOperationException("boom");

        logger.LogInfo("info");
        logger.LogInfo("info", 1);
        logger.LogInfo("info", 1, 2);
        logger.LogInfo("info", 1, 2, 3);

        logger.LogWarn("warn");
        logger.LogWarn("warn", 1);
        logger.LogWarn("warn", 1, 2);
        logger.LogWarn("warn", 1, 2, 3);

        logger.LogErr("err", exception);
        logger.LogErr("err", 1, exception);
        logger.LogErr("err", 1, 2, exception);
        logger.LogErr("err", 1, 2, 3, exception);

        logger.LogDbg("dbg");
        logger.LogDbg("dbg", 1);
        logger.LogDbg("dbg", 1, 2);

        logger.Entries.Should().HaveCount(15);
        logger.Entries.Select(entry => entry.Message).Should().ContainInOrder(
            "info",
            "info 1",
            "info 1 2",
            "info 1 2 3",
            "warn",
            "warn 1",
            "warn 1 2",
            "warn 1 2 3",
            "err",
            "err 1",
            "err 1 2",
            "err 1 2 3",
            "dbg",
            "dbg 1",
            "dbg 1 2");
        logger.Entries.Where(entry => entry.LogLevel == LogLevel.Error)
            .Select(entry => entry.Exception)
            .Should()
            .OnlyContain(entry => ReferenceEquals(entry, exception));
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
