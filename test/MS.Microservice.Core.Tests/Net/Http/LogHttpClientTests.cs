using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Net.Http;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class LogHttpClientTests
{
    [Fact]
    public async Task GetAsync_ShouldConfigureBaseAddress_AndReturnModel()
    {
        var logger = new CapturingLogger<LogHttpClient>();
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ResponsePayload { Message = "ok" })
            }));
        using var httpClient = new HttpClient(handler);
        var client = new LogHttpClient(logger, httpClient);

        client.Configure("https://example.test/", TimeSpan.FromSeconds(5));
        ResponsePayload? result = await client.GetAsync<ResponsePayload>("orders", new QueryPayload { Id = 1, Name = "alice" });

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Message);
        Assert.Equal(new Uri("https://example.test/"), httpClient.BaseAddress);
        Assert.Contains("orders?Id=1&Name=alice", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public async Task PostAsync_WithHeaders_ShouldSendHeadersAndReturnModel()
    {
        var logger = new CapturingLogger<LogHttpClient>();
        var handler = new RecordingHandler(async request =>
        {
            string payload = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"Name\":\"alice\"", payload);
            Assert.True(request.Headers.Contains("X-Trace-Id"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ResponsePayload { Message = "posted" })
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new LogHttpClient(logger, httpClient);

        ResponsePayload? result = await client.PostAsync<ResponsePayload>(
            "orders",
            new QueryPayload { Id = 2, Name = "alice" },
            new Dictionary<string, string> { ["X-Trace-Id"] = "trace-1" });

        Assert.NotNull(result);
        Assert.Equal("posted", result!.Message);
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public async Task GetAsync_WhenJsonIsInvalid_PreservesJsonException()
    {
        var logger = new CapturingLogger<LogHttpClient>();
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{bad json", Encoding.UTF8, "application/json")
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new LogHttpClient(logger, httpClient);

        System.Text.Json.JsonException exception = await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            client.GetAsync<ResponsePayload>("orders", new QueryPayload { Id = 3, Name = "broken" }).AsTask());

        Assert.NotEmpty(exception.Message);
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("broken"));
        Assert.Equal(2, logger.Entries.Count);
    }

    [Theory]
    [InlineData("a&b=c", "a%26b%3Dc")]
    [InlineData("中文 空格", "%E4%B8%AD%E6%96%87%20%E7%A9%BA%E6%A0%BC")]
    [InlineData("+%?#/", "%2B%25%3F%23%2F")]
    [InlineData("", "")]
    public async Task QueryValuesAreEscapedWithoutOverwritingExistingQuery(string value, string encoded)
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal($"https://example.test/orders?fixed=1&Name={encoded}#anchor", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        await new LogHttpClient(new CapturingLogger<LogHttpClient>(), http).GetAsync<object>(
            "orders?fixed=1#anchor", new { Name = value });
    }

    [Fact]
    public async Task NullEmptyCollectionsAndInvariantNumbersHaveDefinedQuerySemantics()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
        try
        {
            var handler = new RecordingHandler(request =>
            {
                Assert.Equal("https://example.test/orders?amount=1.25&items=2&items=3&empty=",
                    request.RequestUri!.AbsoluteUri);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) });
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            await new LogHttpClient(new CapturingLogger<LogHttpClient>(), http).GetAsync<object>(
                "orders?", new Dictionary<string, object?> { ["amount"] = 1.25m, ["items"] = new[] { 2, 3 }, ["nil"] = null, ["empty"] = "" });
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task ConcurrentHeadersStayOnTheirRequestAndJsonUsesCorrectMediaType()
    {
        var handler = new RecordingHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("utf-8", request.Content.Headers.ContentType.CharSet);
                Assert.Equal(await request.Content.ReadAsStringAsync(), "\"" + request.Headers.GetValues("X-Secret").Single() + "\"");
                await Task.Yield();
            }
            else Assert.False(request.Headers.Contains("X-Secret"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new LogHttpClient(global::Microsoft.Extensions.Logging.Abstractions.NullLogger<LogHttpClient>.Instance, http);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => client.PostAsync<object>(
            "orders", index.ToString(), new Dictionary<string, string>() { ["X-Secret"] = index.ToString() })));
        await client.GetAsync<object>("orders", null);
        Assert.False(http.DefaultRequestHeaders.Contains("X-Secret"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpFailuresAndCancellationRetainTheirMeaning(bool post)
    {
        using var http = new HttpClient(new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
            { BaseAddress = new Uri("https://example.test/") };
        var client = new LogHttpClient(new CapturingLogger<LogHttpClient>(), http);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            post ? client.PostAsync<object>("orders", new { }).AsTask() : client.GetAsync<object>("orders", new { }).AsTask());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            post ? client.PostAsync<object>("orders", new { }, cancelled.Token).AsTask()
                : client.GetAsync<object>("orders", new { }, cancelled.Token).AsTask());
    }
    [Fact]
    public async Task CancellationWhileReadingBodyIsNotTranslatedOrLoggedWithBody()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream(reading)) })))
            { BaseAddress = new Uri("https://example.test/") };
        var logger = new CapturingLogger<LogHttpClient>();
        using var cancellation = new CancellationTokenSource();
        var call = new LogHttpClient(logger, http).PostAsync<object>("orders?secret=password", new { Password = "private-data" }, cancellation.Token).AsTask();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain("password", entry.Message);
            Assert.DoesNotContain("private-data", entry.Message);
        });
    }

    private sealed class WaitingStream(TaskCompletionSource reading) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task CopyToAsync(System.IO.Stream destination, int bufferSize, CancellationToken cancellationToken)
            => WaitAsync(cancellationToken);

        private async Task WaitAsync(CancellationToken cancellationToken)
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await responder(request);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class ResponsePayload
    {
        public string? Message { get; set; }
    }

    private sealed class QueryPayload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
