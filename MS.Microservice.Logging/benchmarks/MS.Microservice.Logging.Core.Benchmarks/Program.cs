using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Logging.Core;
using MS.Microservice.Logging.NLog.LayoutRenderers;
using MS.Microservice.Logging.Serilog;
using Serilog.Core;
using Serilog.Events;

const int iterations = 200_000;
const int rounds = 7;

var outer = new RequestLogContext { RequestId = "outer" };
var inner = new RequestLogContext { RequestId = "inner" };

Measure("push/dispose", () =>
{
    using var scope = RequestLogScope.Push(inner);
    return ReferenceEquals(RequestLogScope.Current, inner) ? 1 : 0;
}, 1);

using (RequestLogScope.Push(outer))
{
    Measure("nested push/dispose", () =>
    {
        using var scope = RequestLogScope.Push(inner);
        return ReferenceEquals(RequestLogScope.Current, inner) ? 1 : 0;
    }, 1);
    Measure("read current", () => ReferenceEquals(RequestLogScope.Current, outer) ? 1 : 0, 1);
}

var http = new DefaultHttpContext();
http.Request.Method = HttpMethods.Get;
http.Request.Path = "/orders/42";
http.Request.Headers["requestId"] = "req-001";
http.Request.Headers["platformId"] = "platform-a";
http.Request.Headers["userflag"] = "user-flag";
http.Response.StatusCode = StatusCodes.Status204NoContent;
var completionLogger = new CompletionLogger();
var middleware = new MsRequestLoggingMiddleware(static _ => Task.CompletedTask, TimeProvider.System,
    completionLogger, Options.Create(new AspNetCoreRequestLoggingOptions()));

void InvokeRequest() => middleware.InvokeAsync(http).GetAwaiter().GetResult();

Measure("ASP.NET completion, three headers", () =>
{
    var before = completionLogger.Count;
    InvokeRequest();
    return completionLogger.Count - before;
}, 1, () =>
{
    if (RequestLogScope.Current is not null || completionLogger.EventId != 5000 ||
        !completionLogger.Message!.StartsWith("HTTP GET /orders/42 -> 204 in ", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Unexpected HTTP completion event or scope.");
    }
});

var context = new RequestLogContext
{
    RequestId = "req-001",
    PlatformId = "platform-a",
    UserFlag = "user-flag",
    Method = "GET",
    Path = "/orders/42",
    StatusCode = 204,
    ElapsedMilliseconds = 88,
};
using var requestScope = RequestLogScope.Push(context);
var requestId = new RequestIdLayoutRenderer();
var platformId = new PlatformIdLayoutRenderer();
var userFlag = new UserFlagLayoutRenderer();
var nlogEvent = NLog.LogEventInfo.CreateNullEvent();
Measure("NLog render three headers", () =>
    requestId.Render(nlogEvent).Length + platformId.Render(nlogEvent).Length + userFlag.Render(nlogEvent).Length,
    context.RequestId.Length + context.PlatformId.Length + context.UserFlag.Length, () =>
    {
        if (requestId.Render(nlogEvent) != context.RequestId || platformId.Render(nlogEvent) != context.PlatformId ||
            userFlag.Render(nlogEvent) != context.UserFlag)
        {
            throw new InvalidOperationException("Unexpected NLog request fields.");
        }
    });

var sink = new CountingSink();
using var enabledHost = CreateSerilogHost(LogLevel.Information, sink);
var logger = enabledHost.Services.GetRequiredService<Serilog.ILogger>();
MeasureSerilog("Serilog seven fields", sink, () => logger.Information("request"), 1);
MeasureSerilog("Serilog requestId collision", sink, () => logger.Information("request {requestId}", "event-id"), 1);

var disabledSink = new CountingSink();
using var disabledHost = CreateSerilogHost(LogLevel.None, disabledSink);
var disabledLogger = disabledHost.Services.GetRequiredService<Serilog.ILogger>();
MeasureSerilog("Serilog None, Fatal", disabledSink, () => disabledLogger.Fatal("request"), 0);

static IHost CreateSerilogHost(LogLevel minimumLevel, CountingSink sink) =>
    new HostBuilder().ConfigureMsSerilog(options =>
    {
        options.MinimumLevel = minimumLevel;
        options.ReadFromConfiguration = false;
        options.UseConsoleSink = false;
        options.ConfigureLogger = (_, configuration) => configuration.WriteTo.Sink(sink);
    }).Build();

void MeasureSerilog(string name, CountingSink target, Action write, int expected)
{
    int WriteAndCount()
    {
        var before = target.Count;
        write();
        return target.Count - before;
    }

    Measure(name, WriteAndCount, expected, () =>
    {
        var fieldsMatch = false;
        target.Inspect = logEvent =>
        {
            bool Matches(string key, object? value) =>
                logEvent.Properties.TryGetValue(key, out var property) &&
                property is ScalarValue scalar && Equals(scalar.Value, value);

            fieldsMatch = logEvent.Properties.Count == 7 && Matches("requestId", context.RequestId) &&
                Matches("platformId", context.PlatformId) && Matches("userflag", context.UserFlag) &&
                Matches("requestMethod", context.Method) && Matches("requestPath", context.Path) &&
                Matches("statusCode", 204) && Matches("elapsedMs", 88L);
        };
        var emitted = WriteAndCount();
        target.Inspect = null;
        if (emitted != expected || (expected != 0 && !fieldsMatch))
        {
            throw new InvalidOperationException($"Unexpected Serilog events or fields in {name}.");
        }
    });
}

static void Measure(string name, Func<int> operation, int expected, Action? validate = null)
{
    for (var i = 0; i < iterations / 10; i++)
    {
        _ = operation();
    }

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var observed = 0;
        for (var i = 0; i < iterations; i++)
        {
            observed += operation();
        }
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        if (observed != iterations * expected)
        {
            throw new InvalidOperationException($"Unexpected output in {name}: {observed}.");
        }
        validate?.Invoke();
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op (median of {rounds} x {iterations:N0})");
}

sealed class CompletionLogger : ILogger<MsRequestLoggingMiddleware>
{
    public int Count { get; private set; }
    public int EventId { get; private set; }
    public string? Message { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Count++;
        EventId = eventId.Id;
        Message = formatter(state, exception);
    }
}

sealed class CountingSink : ILogEventSink
{
    public int Count { get; private set; }
    public Action<LogEvent>? Inspect { get; set; }

    public void Emit(LogEvent logEvent)
    {
        Count++;
        Inspect?.Invoke(logEvent);
    }
}
