using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Net.Http;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class LoggingHttpClientHandlerTests
{
    [Fact]
    public async Task SendAsync_ShouldWrapRequestAndResponseContent_AndLogPayloads()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var innerHandler = new RecordingHandler(async request =>
        {
            Assert.IsType<LoggingHttpClientHandler.LoggableHttpContent>(request.Content);
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        });

        using var client = new HttpClient(new LoggingHttpClientHandler(logger) { InnerHandler = innerHandler });
        using var request = new StringContent("{\"name\":\"demo\"}", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("https://example.test/orders", request);
        string requestPayload = await innerHandler.LastRequest!.Content!.ReadAsStringAsync();
        string responsePayload = await response.Content!.ReadAsStringAsync();

        Assert.Equal("{\"name\":\"demo\"}", requestPayload);
        Assert.Equal("{\"ok\":true}", responsePayload);
        Assert.IsType<LoggingHttpClientHandler.LoggableHttpContent>(response.Content);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("\"name\":\"demo\"", logger.Entries[0].Message);
        Assert.Contains("\"ok\":true", logger.Entries[1].Message);
    }

    [Fact]
    public void LazyContentLogger_ShouldHandleNullPlainAndWrappedContent()
    {
        Assert.Equal("null", new LoggingHttpClientHandler.LazyContentLogger(null, CancellationToken.None).ToString());

        var plain = new LoggingHttpClientHandler.LazyContentLogger(
            new StringContent("plain", Encoding.UTF8, "text/plain"),
            CancellationToken.None);
        Assert.Equal("[Non-loggable content]", plain.ToString());

        var wrapped = new LoggingHttpClientHandler.LoggableHttpContent(
            new StringContent("payload", Encoding.UTF8, "text/plain"));
        var lazy = new LoggingHttpClientHandler.LazyContentLogger(wrapped, CancellationToken.None);
        Assert.Equal("payload", lazy.ToString());
    }

    [Fact]
    public async Task LoggableHttpContent_CopyToAsync_ShouldPreservePayloadAndHeaders()
    {
        var content = new LoggingHttpClientHandler.LoggableHttpContent(
            new StringContent("payload", Encoding.UTF8, "text/plain"));

        await using var stream = new MemoryStream();
        await content.CopyToAsync(stream);
        string payload = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Equal("payload", payload);
        Assert.Equal("text/plain", content.Headers.ContentType?.MediaType);
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
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
