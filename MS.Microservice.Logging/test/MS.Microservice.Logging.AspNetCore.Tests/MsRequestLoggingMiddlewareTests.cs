using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Logging.Core;
using Xunit;

namespace MS.Microservice.Logging.AspNetCore.Tests;

public sealed class MsRequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldCaptureHeadersAndEmitCompletionLog()
    {
        var timeProvider = new ControlledTimeProvider(DateTimeOffset.Parse("2026-06-03T00:00:00Z"));
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var options = Options.Create(new AspNetCoreRequestLoggingOptions());

        RequestLogContext? seenContext = null;
        var middleware = new MsRequestLoggingMiddleware(
            next: context =>
            {
                seenContext = RequestLogScope.Current;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                timeProvider.Advance(TimeSpan.FromMilliseconds(123));
                return Task.CompletedTask;
            },
            timeProvider,
            logger,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/orders/42";
        httpContext.Request.Headers["requestId"] = "req-001";
        httpContext.Request.Headers["platformId"] = "platform-a";
        httpContext.Request.Headers["userflag"] = "user-flag";

        await middleware.InvokeAsync(httpContext);

        seenContext.Should().NotBeNull();
        seenContext!.RequestId.Should().Be("req-001");
        seenContext.PlatformId.Should().Be("platform-a");
        seenContext.UserFlag.Should().Be("user-flag");
        seenContext.Method.Should().Be(HttpMethods.Get);
        seenContext.Path.Should().Be("/orders/42");
        seenContext.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        seenContext.ElapsedMilliseconds.Should().Be(123);
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().Be("HTTP GET /orders/42 -> 204 in 123ms");
        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldStillRestoreAmbientContextAndLog_WhenNextThrows()
    {
        var timeProvider = new ControlledTimeProvider(DateTimeOffset.Parse("2026-06-03T00:00:00Z"));
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var options = Options.Create(new AspNetCoreRequestLoggingOptions());
        var expectedException = new InvalidOperationException("boom");

        var middleware = new MsRequestLoggingMiddleware(
            next: _ =>
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(45));
                throw expectedException;
            },
            timeProvider,
            logger,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/failing-endpoint";

        var action = () => middleware.InvokeAsync(httpContext);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().Be("HTTP POST /failing-endpoint -> 200 in 45ms");
        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSkipCompletionLog_WhenDisabled()
    {
        var timeProvider = new ControlledTimeProvider(DateTimeOffset.Parse("2026-06-03T00:00:00Z"));
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var options = Options.Create(new AspNetCoreRequestLoggingOptions
        {
            EmitCompletionLog = false,
        });

        var middleware = new MsRequestLoggingMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                timeProvider.Advance(TimeSpan.FromMilliseconds(30));
                return Task.CompletedTask;
            },
            timeProvider,
            logger,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Put;
        httpContext.Request.Path = "/orders/42";

        await middleware.InvokeAsync(httpContext);

        logger.Entries.Should().BeEmpty();
        RequestLogScope.Current.Should().BeNull();
    }

    private sealed class ControlledTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
            _timestamp += duration.Ticks;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
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