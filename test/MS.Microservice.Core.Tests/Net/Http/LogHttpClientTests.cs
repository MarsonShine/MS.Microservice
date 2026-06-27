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
    public async Task GetAsync_WhenJsonIsInvalid_ShouldThrowWrappedException()
    {
        var logger = new CapturingLogger<LogHttpClient>();
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{bad json", Encoding.UTF8, "application/json")
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new LogHttpClient(logger, httpClient);

        Exception exception = await Assert.ThrowsAsync<Exception>(() =>
            client.GetAsync<ResponsePayload>("orders", new QueryPayload { Id = 3, Name = "broken" }).AsTask());

        Assert.Equal("服务器数据解析异常", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.Equal(2, logger.Entries.Count);
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
