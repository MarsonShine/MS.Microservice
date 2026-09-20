using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MS.Microservice.Core.Net.Http;
using static MS.Microservice.TestSupport.HttpLoggingTestSupport;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class LoggingHttpClientHandlerTests
{
    [Theory]
    [InlineData("", "utf-8")]
    [InlineData("{\"name\":\"demo\"}", "utf-8")]
    [InlineData("中文正文", "utf-8")]
    [InlineData("中文正文", "utf-16")]
    public async Task EnabledLogging_PreservesBodiesHeadersAndOwnership(string body, string charset)
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var encoding = Encoding.GetEncoding(charset);
        var requestContent = new ProbeContent(encoding.GetBytes(body));
        var responseContent = new ProbeContent(encoding.GetBytes(body));
        foreach (var content in new[] { requestContent, responseContent })
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = charset };
            content.Headers.Add("X-Payload", "preserved");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test") { Content = requestContent };
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler(async (message, token) =>
            {
                Assert.Same(requestContent, message.Content);
                Assert.Equal(body, await message.Content!.ReadAsStringAsync(token));
                return new(HttpStatusCode.OK) { Content = responseContent };
            })
        });

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Same(responseContent, response.Content);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.False(requestContent.Disposed);
        Assert.False(responseContent.Disposed);
        foreach (var content in new[] { requestContent, responseContent })
        {
            Assert.Equal(charset, content.Headers.ContentType!.CharSet);
            Assert.Equal("preserved", Assert.Single(content.Headers.GetValues("X-Payload")));
            Assert.Equal(1, content.Serializations);
        }
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(body, Assert.IsType<string>(logger.Entries[0].Fields["@Payload"]));
        Assert.Equal(body, Assert.IsType<string>(logger.Entries[1].Fields["@Response"]));
        Assert.Equal(logger.Entries[0].Fields["Guid"], logger.Entries[1].Fields["Guid"]);

        request.Dispose();
        response.Dispose();
        Assert.True(requestContent.Disposed);
        Assert.True(responseContent.Disposed);
        foreach (var entry in logger.Entries) Assert.Equal(entry.Message, entry.Render());
        Assert.Equal(1, requestContent.Serializations);
        Assert.Equal(1, responseContent.Serializations);
    }

    [Fact]
    public async Task EnabledLogging_HandlesAbsentRequestAndEmptyResponseBodies()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)))
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Null(request.Content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("null", logger.Entries[0].Fields["@Payload"]);
        Assert.Equal("", logger.Entries[1].Fields["@Response"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoggingReadFailure_IsReportedWithoutReplacingContent(bool requestBody)
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var broken = new BrokenContent();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test");
        if (requestBody) request.Content = broken;
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = requestBody ? new StringContent("response") : broken
            }))
        });
        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Same(broken, requestBody ? request.Content : response.Content);
        var entry = logger.Entries[requestBody ? 0 : 1];
        Assert.StartsWith("Error reading content:", Assert.IsType<string>(entry.Fields[requestBody ? "@Payload" : "@Response"]));
    }

    [Fact]
    public async Task ResponseLoggerFailure_DisposesResponseBeforePropagating()
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler> { ThrowOnEntry = 2 };
        var content = new ProbeContent("response"u8.ToArray());
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }))
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request, CancellationToken.None));

        Assert.Equal("Logger failed.", exception.Message);
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransportFailure_IsPreservedAndRequestRemainsCallerOwned(bool enabled)
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler> { Enabled = enabled };
        var content = new ProbeContent("request"u8.ToArray());
        var failure = new HttpRequestException("transport failed");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test") { Content = content };
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromException<HttpResponseMessage>(failure))
        });

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request, CancellationToken.None)));
        Assert.False(content.Disposed);
        Assert.Equal(enabled ? 1 : 0, logger.Entries.Count);
    }

    private sealed class BrokenContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(new IOException("body failed"));
    }
}
