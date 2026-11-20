using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Net.Http;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class HttpClientLogExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HttpEventsRespectInformationLevelAndPreserveStructuredFields(bool enabled)
    {
        var logger = new RecordingLogger(enabled);
        var requestId = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        logger.HttpRequestStarted(requestId, HttpMethod.Post);
        logger.HttpRequestCompleted(requestId, HttpMethod.Post, HttpStatusCode.Created, started);
        logger.HttpRequestFailed(requestId, HttpMethod.Post, new InvalidOperationException("private detail"), started);

        if (!enabled)
        {
            Assert.Empty(logger.Events);
            return;
        }

        Assert.Collection(logger.Events,
            entry => Assert.Equal("HTTP {RequestId} {Method} started", entry["{OriginalFormat}"]),
            entry =>
            {
                Assert.Equal("HTTP {RequestId} {Method} completed {StatusCode} in {ElapsedMs} ms", entry["{OriginalFormat}"]);
                Assert.Equal(201, entry["StatusCode"]);
                Assert.InRange(Assert.IsType<double>(entry["ElapsedMs"]), 0d, double.MaxValue);
            },
            entry =>
            {
                Assert.Equal("HTTP {RequestId} {Method} ended {FailureType} in {ElapsedMs} ms", entry["{OriginalFormat}"]);
                Assert.Equal(nameof(InvalidOperationException), entry["FailureType"]);
                Assert.DoesNotContain("private detail", entry.Values);
                Assert.InRange(Assert.IsType<double>(entry["ElapsedMs"]), 0d, double.MaxValue);
            });
        Assert.All(logger.Events, entry =>
        {
            Assert.Equal(requestId, entry["RequestId"]);
            Assert.Equal("POST", entry["Method"]);
        });
    }

    private sealed class RecordingLogger(bool enabled) : ILogger
    {
        public List<Dictionary<string, object?>> Events { get; } = [];
        public bool IsEnabled(LogLevel level) => enabled && level == LogLevel.Information;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Information, level);
            Assert.Null(exception);
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Events.Add(fields.ToDictionary(field => field.Key, field => field.Value));
        }
    }
}
