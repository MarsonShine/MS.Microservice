using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIConcurrencyLeaseTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly AIResolvedModel Model = new() { Provider = "test", Model = "test" };
    private static readonly AIChatRequest Request = new() { Messages = [] };
    private const string ChatJson = """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""";
    private const string StreamText = "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n";

    [Theory]
    [InlineData("chat")]
    [InlineData("stream")]
    [InlineData("media")]
    public async Task FailureAfterAcquiringSlotBeforeRequestStillReleasesSlot(string path)
    {
        var clock = new FailFirstTimestamp();
        using var handler = new ResponseHandler(_ => Response(path == "stream" ? StreamText : ChatJson));
        using var client = new HttpClient(handler);
        var factory = new ClientFactory(client);
        var chat = new ChatProvider(factory, clock);
        var media = new MediaProvider(factory, clock);

        async Task Invoke(CancellationToken token)
        {
            if (path == "media") Assert.Equal(42, await media.Execute(token));
            else if (path == "chat") Assert.Equal("ok", (await chat.GetResponseAsync(Model, Request, token)).Text);
            else
            {
                var chunks = new List<AIChatStreamChunk>();
                await foreach (var chunk in chat.StreamAsync(Model, Request, token)) chunks.Add(chunk);
                Assert.Equal("ok", chunks[0].DeltaText);
                Assert.True(chunks[^1].IsFinal);
            }
        }

        Assert.Same(clock.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(default)));
        Assert.Equal(0, handler.Calls);
        using var deadline = new CancellationTokenSource(TestTimeout);
        await Invoke(deadline.Token);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task StreamDisposalFailureStillReleasesSlotForNextRequest()
    {
        var failure = new InvalidOperationException("response disposal failed");
        using var handler = new ResponseHandler(call => call == 1
            ? new ThrowingResponse(failure) { Content = new StringContent(StreamText) }
            : Response(ChatJson));
        using var client = new HttpClient(handler);
        var provider = new ChatProvider(new ClientFactory(client), TimeProvider.System);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var stream = provider.StreamAsync(Model, Request, deadline.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => stream.DisposeAsync().AsTask()));

        Assert.Equal("ok", (await provider.GetResponseAsync(Model, Request, deadline.Token)).Text);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task StreamHoldsSlotUntilDisposedAndCanceledWaiterDoesNotReleaseIt()
    {
        using var handler = new ResponseHandler(call => Response(call == 1 ? StreamText : ChatJson));
        using var client = new HttpClient(handler);
        var provider = new ChatProvider(new ClientFactory(client), TimeProvider.System);
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var stream = provider.StreamAsync(Model, Request, deadline.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());

        using var canceledWaiter = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var waiting = provider.GetResponseAsync(Model, Request, canceledWaiter.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        canceledWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        var next = provider.GetResponseAsync(Model, Request, deadline.Token).AsTask();
        Assert.False(next.IsCompleted);
        Assert.Equal(1, handler.Calls);

        await stream.DisposeAsync();
        Assert.Equal("ok", (await next).Text);
        Assert.Equal(2, handler.Calls);
    }

    private static HttpResponseMessage Response(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static IOptions<AIOptions> CreateOptions()
    {
        var options = new AIOptions();
        options.Providers["test"] = new() { ConcurrencyLimit = 1, ApiKey = "test" };
        return Options.Create(options);
    }

    private sealed class ChatProvider(IHttpClientFactory factory, TimeProvider clock)
        : OpenAICompatibleChatProviderBase(factory, CreateOptions(), clock, NullLogger.Instance)
    {
        public override string Name => "test";
        protected override string HttpClientName => "test";
        protected override string DefaultBaseAddress => "https://test.invalid/";
    }

    private sealed class MediaProvider(IHttpClientFactory factory, TimeProvider clock)
        : OpenAICompatibleMediaProviderBase(factory, CreateOptions(), clock, NullLogger.Instance)
    {
        public override string Name => "test";
        protected override string HttpClientName => "test";
        protected override string DefaultBaseAddress => "https://test.invalid/";

        public Task<int> Execute(CancellationToken token) => ExecuteAsync(
            AICapability.ImageGeneration, "image", Model, null,
            () => CreateJsonRequest("images/generations", new { prompt = "test" }),
            (_, _) => Task.FromResult(42), token);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ResponseHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(Interlocked.Increment(ref _calls)));
    }

    private sealed class FailFirstTimestamp : TimeProvider
    {
        private int _calls;
        public InvalidOperationException Failure { get; } = new("timestamp unavailable");
        public override long GetTimestamp()
            => Interlocked.Increment(ref _calls) == 1 ? throw Failure : base.GetTimestamp();
    }

    private sealed class ThrowingResponse(Exception failure) : HttpResponseMessage(HttpStatusCode.OK)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) throw failure;
        }
    }
}
