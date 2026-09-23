using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Net.Http;

const int rounds = 5;

Measure("Information disabled, 512 B each way", enabled: false, redacted: false, size: 512, iterations: 5_000);
Measure("Body logging, 512 B each way", enabled: true, redacted: false, size: 512, iterations: 5_000);
Measure("Body logging, 16 KiB each way", enabled: true, redacted: false, size: 16 * 1024, iterations: 1_000);
Measure("Redacted metadata, 512 B each way", enabled: true, redacted: true, size: 512, iterations: 5_000);

static void Measure(string name, bool enabled, bool redacted, int size, int iterations)
{
    var payload = Encoding.UTF8.GetBytes(new string('x', size));
    var logger = new FormattingLogger(enabled);
    using var invoker = new HttpMessageInvoker(new LoggingHttpClientHandler(
        logger, Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = redacted }))
    {
        InnerHandler = new InMemoryHandler(payload)
    });

    RunOnce(invoker, payload);
    if (enabled && logger.RenderedCount != (redacted ? 1 : 2))
        throw new InvalidOperationException("The logger did not format the expected messages.");
    if (enabled && !redacted && !logger.LastMessage!.Contains(new string('x', size), StringComparison.Ordinal))
        throw new InvalidOperationException("The response body was not formatted into the log.");

    for (var i = 0; i < Math.Min(iterations / 10, 500); i++) RunOnce(invoker, payload);

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) RunOnce(invoker, payload);
        times[round] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / iterations;
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op (median of {rounds} x {iterations:N0})");
}

static void RunOnce(HttpMessageInvoker invoker, byte[] payload)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders?legacy=value")
    {
        Content = new StreamContent(new MemoryStream(payload, writable: false))
    };
    using var response = invoker.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    response.Content.CopyToAsync(Stream.Null).GetAwaiter().GetResult();
}

sealed class InMemoryHandler(byte[] responsePayload) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await request.Content!.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(responsePayload, writable: false))
        };
    }
}

sealed class FormattingLogger(bool enabled) : ILogger<LoggingHttpClientHandler>
{
    public int RenderedCount { get; private set; }
    public string? LastMessage { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => enabled && logLevel == LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        LastMessage = formatter(state, exception);
        RenderedCount++;
    }
}
