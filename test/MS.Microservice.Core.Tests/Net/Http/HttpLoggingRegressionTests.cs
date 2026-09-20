using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Core.Net.Http;
using static MS.Microservice.TestSupport.HttpLoggingTestSupport;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class HttpLoggingRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisabledLogging_StreamsRequestWithoutReplacingOrBufferingContent()
    {
        var content = new ProbeContent("first-last"u8.ToArray(), gated: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test") { Content = content };
        using var destination = new MemoryStream();
        HttpContent? forwarded = null;
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(NullLogger<LoggingHttpClientHandler>.Instance)
        {
            InnerHandler = new CallbackHandler(async (message, token) =>
            {
                forwarded = message.Content;
                await message.Content!.CopyToAsync(destination, token);
                return new(HttpStatusCode.OK);
            })
        });

        var sending = client.SendAsync(request, CancellationToken.None);
        try
        {
            await content.Started.Task.WaitAsync(Timeout);
            Assert.Same(content, forwarded);
            Assert.Equal(content.Prefix.ToArray(), destination.ToArray());
            Assert.False(sending.IsCompleted);
        }
        finally
        {
            content.Release.TrySetResult();
            using var response = await sending.WaitAsync(Timeout);
        }
        Assert.Equal("first-last"u8.ToArray(), destination.ToArray());
    }

    [Fact]
    public async Task DisabledLogging_StreamsResponseWithoutReplacingOrBufferingContent()
    {
        var content = new ProbeContent("first-last"u8.ToArray(), gated: true);
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(NullLogger<LoggingHttpClientHandler>.Instance)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }))
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(0, content.Serializations);
        using var destination = new MemoryStream();
        var copying = response.Content.CopyToAsync(destination);
        try
        {
            await content.Started.Task.WaitAsync(Timeout);
            Assert.Same(content, response.Content);
            Assert.Equal(content.Prefix.ToArray(), destination.ToArray());
            Assert.False(copying.IsCompleted);
        }
        finally
        {
            content.Release.TrySetResult();
            await copying.WaitAsync(Timeout);
        }
        Assert.Equal("first-last"u8.ToArray(), destination.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnabledLogging_ReturnsAnIncompleteTaskWhileBodyIsPending(bool requestBody)
    {
        var logger = new CapturingLogger<LoggingHttpClientHandler>();
        var content = new ProbeContent("first-last"u8.ToArray(), gated: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test");
        if (requestBody) request.Content = content;
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(logger)
        {
            InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = requestBody ? new StringContent("response") : content
            }))
        });
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Isolate the legacy sync wait so its regression cannot block the test runner.
        var sending = Task.Run(async () =>
        {
            var pending = client.SendAsync(request, CancellationToken.None);
            returned.TrySetResult();
            return await pending;
        });
        try
        {
            await content.Started.Task.WaitAsync(Timeout);
            await returned.Task.WaitAsync(Timeout);
            Assert.False(sending.IsCompleted);
        }
        finally
        {
            content.Release.TrySetResult();
            using var response = await sending.WaitAsync(Timeout);
        }
        Assert.Equal(2, logger.Entries.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellation_PropagatesAndDisposesOnlyAnUnreturnedResponse(bool requestBody)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new ProbeContent("pending-body"u8.ToArray(), gated: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test");
        if (requestBody) request.Content = content;
        int transportCalls = 0;
        using var client = new HttpMessageInvoker(new LoggingHttpClientHandler(new CapturingLogger<LoggingHttpClientHandler>())
        {
            InnerHandler = new CallbackHandler((_, _) =>
            {
                transportCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = requestBody ? new StringContent("response") : content
                });
            })
        });
        var sending = Task.Run(() => client.SendAsync(request, cancellation.Token));
        try
        {
            await content.Started.Task.WaitAsync(Timeout);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending.WaitAsync(Timeout));
            Assert.Equal(requestBody ? 0 : 1, transportCalls);
            Assert.Equal(!requestBody, content.Disposed);
        }
        finally
        {
            content.Release.TrySetResult();
            try { using var unexpected = await sending.WaitAsync(Timeout); }
            catch (OperationCanceledException) { }
        }
    }
}
