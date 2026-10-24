using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Logging.Core;
using Xunit;

namespace MS.Microservice.Logging.AspNetCore.Tests;

public sealed class MsRequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldExpireAmbientContextInDetachedChild()
    {
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RequestLogContext?>? child = null;
        var middleware = new MsRequestLoggingMiddleware(
            next: _ =>
            {
                child = Task.Run(async () =>
                {
                    await releaseChild.Task;
                    return RequestLogScope.Current;
                });
                return Task.CompletedTask;
            },
            TimeProvider.System,
            new TestLogger<MsRequestLoggingMiddleware>(),
            Options.Create(new AspNetCoreRequestLoggingOptions { EmitCompletionLog = false }));

        await middleware.InvokeAsync(new DefaultHttpContext());
        releaseChild.SetResult();

        (await child!).Should().BeNull();
    }

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
        logger.Entries[0].EventId.Id.Should().Be(5000);
        logger.Entries[0].LogLevel.Should().Be(LogLevel.Information);
        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldStillRestoreAmbientContextAndLog_WhenNextThrows()
    {
        var timeProvider = new ControlledTimeProvider(DateTimeOffset.Parse("2026-06-03T00:00:00Z"));
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var options = Options.Create(new AspNetCoreRequestLoggingOptions());
        var expectedException = new InvalidOperationException("boom");
        RequestLogContext? seenContext = null;

        var middleware = new MsRequestLoggingMiddleware(
            next: _ =>
            {
                seenContext = RequestLogScope.Current;
                timeProvider.Advance(TimeSpan.FromMilliseconds(45));
                throw expectedException;
            },
            timeProvider,
            logger,
            options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/failing-endpoint";

        Assert.Same(expectedException, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext)));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(5001, entry.EventId.Id);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Null(entry.Fields["StatusCode"]);
        Assert.Equal(nameof(InvalidOperationException), entry.Fields["FailureType"]);
        Assert.Equal(HttpMethods.Post, entry.Fields["Method"]);
        Assert.Equal("/failing-endpoint", entry.Fields["Path"]);
        Assert.Equal(45L, entry.Fields["ElapsedMs"]);
        Assert.Null(entry.Exception);
        Assert.NotNull(seenContext);
        Assert.Null(seenContext.StatusCode);
        RequestLogScope.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task InvokeAsync_HandledErrorResponseUsesTheCompletionEvent(int statusCode)
    {
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var middleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(new DefaultHttpContext());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(5000, entry.EventId.Id);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal(statusCode, entry.Fields["StatusCode"]);
        Assert.Null(RequestLogScope.Current);
    }

    [Fact]
    public async Task InvokeAsync_FailureAfterResponseStartedRetainsTheSentStatus()
    {
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        var expected = new InvalidOperationException("private failure detail");
        var middleware = CreateMiddleware(_ => throw expected, logger);
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature { StatusCode = StatusCodes.Status202Accepted });

        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context)));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(5001, entry.EventId.Id);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal(StatusCodes.Status202Accepted, entry.Fields["StatusCode"]);
        Assert.Equal(nameof(InvalidOperationException), entry.Fields["FailureType"]);
        Assert.Null(entry.Exception);
        Assert.Null(RequestLogScope.Current);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvokeAsync_RequestCancellationUsesTheCancellationEvent(bool throwsCancellation, bool responseStarted)
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException("private cancellation detail", cancellation.Token);
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        RequestLogContext? seenContext = null;
        var middleware = CreateMiddleware(context =>
        {
            seenContext = RequestLogScope.Current;
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            cancellation.Cancel();
            if (throwsCancellation) throw expected;
            return Task.CompletedTask;
        }, logger);
        var httpContext = new DefaultHttpContext { RequestAborted = cancellation.Token };
        if (responseStarted) httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        if (throwsCancellation)
            Assert.Same(expected, await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(httpContext)));
        else
            await middleware.InvokeAsync(httpContext);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(5002, entry.EventId.Id);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal(responseStarted ? StatusCodes.Status206PartialContent : (int?)null, entry.Fields["StatusCode"]);
        Assert.Equal(throwsCancellation ? nameof(OperationCanceledException) : null, entry.Fields["FailureType"]);
        Assert.Null(entry.Exception);
        Assert.NotNull(seenContext);
        Assert.Equal(responseStarted ? StatusCodes.Status206PartialContent : (int?)null, seenContext.StatusCode);
        Assert.Null(RequestLogScope.Current);
    }

    [Fact]
    public async Task InvokeAsync_FailureIsLoggedWhenInformationIsDisabledButWarningIsEnabled()
    {
        var logger = new TestLogger<MsRequestLoggingMiddleware>(LogLevel.Warning);
        var expected = new InvalidOperationException("private failure detail");
        var middleware = CreateMiddleware(_ => throw expected, logger);

        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext())));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(5001, entry.EventId.Id);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Null(RequestLogScope.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_InformationCompletionEventsRespectTheLoggerLevel(bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        var logger = new TestLogger<MsRequestLoggingMiddleware>(LogLevel.Warning);
        var middleware = CreateMiddleware(_ =>
        {
            if (canceled) cancellation.Cancel();
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(new DefaultHttpContext { RequestAborted = cancellation.Token });

        Assert.Empty(logger.Entries);
        Assert.Null(RequestLogScope.Current);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    [InlineData("aborted")]
    public async Task InvokeAsync_DisabledCompletionLoggingAlsoSuppressesFailureAndCancellation(string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        var logger = new TestLogger<MsRequestLoggingMiddleware>();
        Exception? expected = outcome switch
        {
            "failed" => new InvalidOperationException("failure"),
            "canceled" => new OperationCanceledException(cancellation.Token),
            _ => null
        };
        var middleware = CreateMiddleware(_ =>
        {
            if (outcome != "failed") cancellation.Cancel();
            if (expected is not null) throw expected;
            return Task.CompletedTask;
        }, logger, emitCompletionLog: false);

        var actual = await Record.ExceptionAsync(() => middleware.InvokeAsync(new DefaultHttpContext { RequestAborted = cancellation.Token }));

        Assert.Same(expected, actual);
        Assert.Empty(logger.Entries);
        Assert.Null(RequestLogScope.Current);
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

    private static MsRequestLoggingMiddleware CreateMiddleware(RequestDelegate next,
        TestLogger<MsRequestLoggingMiddleware> logger, bool emitCompletionLog = true)
        => new(next, TimeProvider.System, logger,
            Options.Create(new AspNetCoreRequestLoggingOptions { EmitCompletionLog = emitCompletionLog }));

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
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

    private sealed class TestLogger<T>(LogLevel minimumLevel = LogLevel.Trace) : ILogger<T>
    {
        public List<TestLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new TestLogEntry(logLevel, eventId, formatter(state, exception), exception, fields));
        }
    }

    private sealed record TestLogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
