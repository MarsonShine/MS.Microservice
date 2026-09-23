using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.Net.Http;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class LoggingHttpClientHandlerTests
{
    [Fact]
    public async Task ConfiguredOptionsEnableRedactionThroughDependencyInjection()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LoggingHttpClientHandler>>(logger);
        services.Configure<LoggingHttpClientHandlerOptions>(options => options.EnableRedaction = true);
        services.AddTransient<LoggingHttpClientHandler>();
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<LoggingHttpClientHandler>();
        handler.InnerHandler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/orders?token=private-query");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(logger.Entries);
        Assert.Contains("GET /orders responded 204", logger.Entries[0].Message);
        Assert.DoesNotContain("private-query", logger.Entries[0].Message);
    }

    [Fact]
    public async Task RedactionEnabled_LogsOnlyMetadataAndLeavesContentUnwrapped()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        using var requestContent = new StringContent("request-private-value", Encoding.UTF8, "application/json");
        using var responseContent = new StringContent("response-private-value", Encoding.UTF8, "application/json");
        var innerHandler = new RecordingHandler(request =>
        {
            Assert.Same(requestContent, request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = responseContent });
        });
        using var client = new HttpClient(new LoggingHttpClientHandler(logger,
            Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = true })) { InnerHandler = innerHandler });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://user:password@example.test/orders/42?token=query-private-value#fragment") { Content = requestContent };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Same(responseContent, response.Content);
        Assert.Single(logger.Entries);
        var log = logger.Entries[0].Message;
        Assert.Contains("POST /orders/42 responded 202", log);
        Assert.Contains(" ms", log);
        Assert.DoesNotContain("request-private-value", log);
        Assert.DoesNotContain("response-private-value", log);
        Assert.DoesNotContain("query-private-value", log);
        Assert.DoesNotContain("password", log);
        Assert.DoesNotContain("example.test", log);
        Assert.DoesNotContain("fragment", log);
    }

    [Fact]
    public async Task RedactionEnabled_RemovesQueryAndFragmentFromRelativeUri()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var innerHandler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = new HttpMessageInvoker(new LoggingHttpClientHandler(logger,
            Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = true })) { InnerHandler = innerHandler });
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("/relative/orders?token=private-query#fragment", UriKind.Relative));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(logger.Entries);
        Assert.Contains("GET /relative/orders responded 200", logger.Entries[0].Message);
        Assert.DoesNotContain("private-query", logger.Entries[0].Message);
        Assert.DoesNotContain("fragment", logger.Entries[0].Message);
    }

    [Fact]
    public async Task RedactionEnabled_DoesNotReadRequestOrResponseStreamsForLogging()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        using var requestContent = new ProbeContent();
        using var responseContent = new ProbeContent();
        var innerHandler = new RecordingHandler(request =>
        {
            Assert.Same(requestContent, request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent });
        });
        using var client = new HttpClient(new LoggingHttpClientHandler(logger,
            Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = true })) { InnerHandler = innerHandler });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/stream") { Content = requestContent };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Same(responseContent, response.Content);
        Assert.Equal(0, requestContent.SerializationCount);
        Assert.Equal(0, responseContent.SerializationCount);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task RedactionEnabled_LogsFailureTypeWithoutExceptionMessageOrQuery()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var failure = new InvalidOperationException("private-failure-message");
        var innerHandler = new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(failure));
        using var client = new HttpClient(new LoggingHttpClientHandler(logger,
            Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = true })) { InnerHandler = innerHandler });

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("https://example.test/fail?token=private-query"));

        Assert.Same(failure, observed);
        Assert.Single(logger.Entries);
        Assert.Contains("GET /fail failed InvalidOperationException", logger.Entries[0].Message);
        Assert.DoesNotContain("private-failure-message", logger.Entries[0].Message);
        Assert.DoesNotContain("private-query", logger.Entries[0].Message);
    }

    [Fact]
    public async Task RedactionEnabled_PreservesCancellationAndLogsMetadata()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        using var cancellation = new CancellationTokenSource();
        var innerHandler = new RecordingHandler(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        using var client = new HttpClient(new LoggingHttpClientHandler(logger,
            Options.Create(new LoggingHttpClientHandlerOptions { EnableRedaction = true })) { InnerHandler = innerHandler });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync("https://example.test/cancel?token=private-query", cancellation.Token));

        Assert.Single(logger.Entries);
        Assert.Contains("GET /cancel canceled", logger.Entries[0].Message);
        Assert.DoesNotContain("private-query", logger.Entries[0].Message);
    }

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

        HttpResponseMessage response = await client.PostAsync("https://example.test/orders?token=legacy-query", request);
        string requestPayload = await innerHandler.LastRequest!.Content!.ReadAsStringAsync();
        string responsePayload = await response.Content!.ReadAsStringAsync();

        Assert.Equal("{\"name\":\"demo\"}", requestPayload);
        Assert.Equal("{\"ok\":true}", responsePayload);
        Assert.IsType<LoggingHttpClientHandler.LoggableHttpContent>(response.Content);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("\"name\":\"demo\"", logger.Entries[0].Message);
        Assert.Contains("legacy-query", logger.Entries[0].Message);
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

    private sealed class ProbeContent : HttpContent
    {
        public int SerializationCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializationCount++;
            throw new InvalidOperationException("Content was read for logging.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
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
