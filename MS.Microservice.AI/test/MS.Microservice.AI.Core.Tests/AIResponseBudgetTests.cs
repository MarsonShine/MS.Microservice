using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIResponseBudgetTests
{
    private const string ChatJson = """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConfiguredResponseBudgetRejectsOversizedChatWithoutRetry()
    {
        using var handler = new ResponseHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(ChatJson.Replace("ok", new string('x', 256), StringComparison.Ordinal))
        });
        using var services = CreateContainer(handler, new() { ["MaxResponseBytes"] = "128" });
        var client = services.GetRequiredService<IAIChatClient>();

        var error = await Assert.ThrowsAsync<AIProviderException>(() => client.GetResponseAsync(ChatRequest()).AsTask());

        Assert.Equal(AIErrorCodes.ResponseInvalid, error.ErrorCode);
        Assert.False(error.IsTransient);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("chat", false, false)]
    [InlineData("tts", true, false)]
    [InlineData("chat", true, true)]
    public async Task ActualSuccessBytesAreBoundedRegardlessOfLengthHeaderOrExistingBuffer(
        string capability, bool underreportedLength, bool prebuffered)
    {
        var bytes = capability == "tts" ? new byte[512]
            : Encoding.UTF8.GetBytes(ChatJson.Replace("ok", new string('x', 256), StringComparison.Ordinal));
        using var source = new ForwardOnlyStream(bytes);
        var content = new StreamContent(source, 16);
        if (underreportedLength) content.Headers.ContentLength = 1;
        if (prebuffered) await content.LoadIntoBufferAsync();
        using var response = new TrackedResponse(HttpStatusCode.OK, content);
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new() { ["MaxResponseBytes"] = "128" });
        using var deadline = new CancellationTokenSource(TestTimeout);

        var error = await Assert.ThrowsAsync<AIProviderException>(() => Invoke(services, capability, deadline.Token));

        AssertBudgetError(error, capability == "tts" ? AICapability.Tts : AICapability.Chat, HttpStatusCode.OK);
        Assert.Equal(1, handler.Calls);
        Assert.True(response.Disposed);
        Assert.True(source.Disposed);
        if (!prebuffered) Assert.True(source.BytesRead < bytes.Length, "The reader must stop before consuming the whole oversized body.");
        await Invoke(services, "tts", deadline.Token);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("chat", HttpStatusCode.TooManyRequests, false)]
    [InlineData("tts", HttpStatusCode.InternalServerError, false)]
    [InlineData("stream", HttpStatusCode.InternalServerError, true)]
    public async Task ErrorBodiesAndStreamingHandshakeUseTheirOwnBudgetWithoutRetry(
        string capability, HttpStatusCode status, bool prebuffered)
    {
        using var source = new ForwardOnlyStream(Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"" + new string('x', 256) + "\"}}"));
        var content = new StreamContent(source, 16);
        content.Headers.ContentLength = 1;
        if (prebuffered) await content.LoadIntoBufferAsync();
        using var response = new TrackedResponse(status, content);
        response.Headers.RetryAfter = new(TimeSpan.Zero);
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new()
        {
            ["MaxResponseBytes"] = "4096", ["MaxErrorResponseBytes"] = "64"
        });
        using var deadline = new CancellationTokenSource(TestTimeout);

        var error = await Assert.ThrowsAsync<AIProviderException>(() => Invoke(services, capability, deadline.Token));

        AssertBudgetError(error, capability == "tts" ? AICapability.Tts : AICapability.Chat, status);
        Assert.Equal(1, handler.Calls);
        Assert.True(response.Disposed);
        await Invoke(services, "tts", deadline.Token);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Utf8ResponseExactlyAtByteBudgetSucceedsAndOneByteLessIsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes(ChatJson.Replace("ok", "你好🙂", StringComparison.Ordinal));
        foreach (var budget in new[] { bytes.Length, bytes.Length - 1 })
        {
            using var source = new ForwardOnlyStream(bytes, segmentSize: 1);
            using var response = new TrackedResponse(HttpStatusCode.OK, new StreamContent(source));
            using var handler = new ResponseHandler(_ => response);
            using var services = CreateContainer(handler, new() { ["MaxResponseBytes"] = budget.ToString() });
            var request = services.GetRequiredService<IAIChatClient>().GetResponseAsync(ChatRequest()).AsTask();

            if (budget == bytes.Length) Assert.Equal("你好🙂", (await request).Text);
            else Assert.Equal(AIErrorCodes.ResponseInvalid, (await Assert.ThrowsAsync<AIProviderException>(() => request)).ErrorCode);
            Assert.Equal(1, handler.Calls);
            Assert.True(response.Disposed);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task StreamLineLimitPreservesBomUnicodeNewlinesAndUnterminatedDoneMarker(string newline)
    {
        var line = DeltaLine("你好🙂");
        var body = "\uFEFF" + ":" + new string('x', line.Length - 1) + newline + line + newline + "data: [DONE]";
        using var source = new ForwardOnlyStream(Encoding.UTF8.GetBytes(body), segmentSize: 1);
        using var response = new TrackedResponse(HttpStatusCode.OK, new StreamContent(source));
        using var handler = new ResponseHandler(_ => response);
        using var services = CreateContainer(handler, new()
        {
            ["MaxResponseBytes"] = "8", ["MaxStreamLineCharacters"] = line.Length.ToString()
        });

        var chunks = new List<AIChatStreamChunk>();
        await foreach (var chunk in services.GetRequiredService<IAIChatClient>().StreamAsync(ChatRequest())) chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("你好🙂", chunks[0].DeltaText);
        Assert.True(chunks[1].IsFinal);
        Assert.True(response.Disposed);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("data:")]
    [InlineData(":")]
    [InlineData(" ")]
    public async Task StreamRejectsOverlongDataCommentAndWhitespaceLinesBeforeParsingOrIgnoringThem(string prefix)
    {
        var line = prefix + new string(prefix == " " ? ' ' : 'x', 129 - prefix.Length);
        using var source = new ForwardOnlyStream(Encoding.UTF8.GetBytes(line));
        using var response = new TrackedResponse(HttpStatusCode.OK, new StreamContent(source));
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new() { ["MaxStreamLineCharacters"] = "128" });
        using var deadline = new CancellationTokenSource(TestTimeout);

        var error = await Assert.ThrowsAsync<AIProviderException>(() => Invoke(services, "stream", deadline.Token));

        AssertBudgetError(error, AICapability.Chat, HttpStatusCode.OK);
        Assert.Equal(1, handler.Calls);
        Assert.True(response.Disposed);
        await Invoke(services, "tts", deadline.Token);
    }

    [Fact]
    public async Task StreamLimitAfterFirstChunkDoesNotReplayOutputOrYieldAFinalChunk()
    {
        var line = DeltaLine("first");
        var body = line + "\n:" + new string('x', line.Length) + "\n";
        using var response = new TrackedResponse(HttpStatusCode.OK, new StringContent(body));
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new() { ["MaxStreamLineCharacters"] = line.Length.ToString() });
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var stream = services.GetRequiredService<IAIChatClient>().StreamAsync(ChatRequest(), deadline.Token).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("first", stream.Current.DeltaText);
        Assert.False(stream.Current.IsFinal);
        var error = await Assert.ThrowsAsync<AIProviderException>(() => stream.MoveNextAsync().AsTask());

        Assert.Equal(AIErrorCodes.ResponseInvalid, error.ErrorCode);
        Assert.False(error.IsTransient);
        Assert.Equal(1, handler.Calls);
        Assert.True(response.Disposed);
        await Invoke(services, "tts", deadline.Token);
    }

    [Fact]
    public async Task CompleteFirstEventIsDeliveredWhenNextNetworkReadWouldStall()
    {
        // Filling StreamReader's internal byte buffer must not make it wait to fill a larger character buffer.
        var body = (DeltaLine("first") + "\n").PadRight(1024, ' ');
        using var source = new ForwardOnlyStream(Encoding.UTF8.GetBytes(body), segmentSize: 1024, stallAfterData: true);
        using var response = new TrackedResponse(HttpStatusCode.OK, new StreamContent(source));
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new() { ["MaxStreamLineCharacters"] = "2048" });
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var stream = services.GetRequiredService<IAIChatClient>().StreamAsync(ChatRequest(), deadline.Token).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("first", stream.Current.DeltaText);
        Assert.False(source.Stalled.Task.IsCompleted);
        await stream.DisposeAsync();

        Assert.True(response.Disposed);
        await Invoke(services, "tts", deadline.Token);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("chat", false)]
    [InlineData("tts", true)]
    [InlineData("stream", false)]
    public async Task BodyAfterHeadersRemainsCancelableAndTimedOutWithResponseAndSlotReleased(string capability, bool callerCancels)
    {
        using var source = new ForwardOnlyStream([], stallAfterData: true);
        using var response = new TrackedResponse(capability == "stream" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            new StreamContent(source));
        using var handler = new ResponseHandler(call => call == 1 ? response : SpeechResponse());
        using var services = CreateContainer(handler, new() { ["MaxRetryAttempts"] = "0" });
        using var deadline = new CancellationTokenSource(TestTimeout);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var call = Invoke(services, capability, caller.Token, callerCancels ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(200));
        await source.Stalled.Task.WaitAsync(deadline.Token);

        if (callerCancels)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
        else await Assert.ThrowsAsync<AITimeoutException>(() => call);
        Assert.Equal(1, handler.Calls);
        Assert.True(response.Disposed);
        Assert.True(source.Disposed);
        await Invoke(services, "tts", deadline.Token);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("MaxResponseBytes", "0")]
    [InlineData("MaxResponseBytes", "-1")]
    [InlineData("MaxResponseBytes", "2147483648")]
    [InlineData("MaxErrorResponseBytes", "0")]
    [InlineData("MaxErrorResponseBytes", "2147483648")]
    [InlineData("MaxStreamLineCharacters", "0")]
    [InlineData("MaxStreamLineCharacters", "-1")]
    public void InvalidResponseBudgetsFailStartupValidation(string option, string value)
    {
        using var handler = new ResponseHandler(_ => SpeechResponse());
        using var services = CreateContainer(handler, new() { [option] = value });

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(failure.Failures, message => message.Contains("AI:Providers:OpenAI:" + option, StringComparison.Ordinal));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void ResponseBudgetValidationAcceptsInclusiveLimits()
    {
        using var handler = new ResponseHandler(_ => SpeechResponse());
        using var services = CreateContainer(handler, new()
        {
            ["MaxResponseBytes"] = "2147483647", ["MaxErrorResponseBytes"] = "1", ["MaxStreamLineCharacters"] = "1"
        });
        services.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(0, handler.Calls);
    }

    private static void AssertBudgetError(AIProviderException error, AICapability capability, HttpStatusCode status)
    {
        Assert.Equal(AIErrorCodes.ResponseInvalid, error.ErrorCode);
        Assert.False(error.IsTransient);
        Assert.Equal(capability, error.Capability);
        Assert.Equal("OpenAI", error.Provider);
        Assert.Equal("test", error.Model);
        Assert.Equal("caller-id", error.RequestId);
        Assert.Equal("provider-id", error.ProviderRequestId);
        Assert.Equal((int)status, error.StatusCode);
    }

    private static string DeltaLine(string text) => "data: {\"choices\":[{\"delta\":{\"content\":\"" + text + "\"}}]}";

    private static HttpResponseMessage SpeechResponse() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };

    private static async Task Invoke(IServiceProvider services, string capability, CancellationToken token, TimeSpan? timeout = null)
    {
        if (capability == "chat")
            await services.GetRequiredService<IAIChatClient>().GetResponseAsync(ChatRequest() with { Timeout = timeout }, token);
        else if (capability == "tts")
            await services.GetRequiredService<IAITtsClient>().SynthesizeAsync(new()
            {
                Provider = "OpenAI", Model = "test", Voice = "alloy", Input = "hello", RequestId = "caller-id", Timeout = timeout
            }, token);
        else
            await foreach (var _ in services.GetRequiredService<IAIChatClient>().StreamAsync(ChatRequest() with { Timeout = timeout }, token)) { }
    }

    private static AIChatRequest ChatRequest() => new()
    {
        Provider = "OpenAI", Model = "test", Messages = [new("user", "hello")], RequestId = "caller-id"
    };

    private static ServiceProvider CreateContainer(HttpMessageHandler handler, Dictionary<string, string?>? limits = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Providers:OpenAI:ApiKey"] = "test-key",
            ["AI:Providers:OpenAI:ConcurrencyLimit"] = "1",
            ["AI:Providers:OpenAI:MaxRetryAttempts"] = "2"
        };
        foreach (var entry in limits ?? []) values["AI:Providers:OpenAI:" + entry.Key] = entry.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddOpenAI();
        services.AddSingleton<IHttpClientFactory>(_ => new ClientFactory(handler));
        return services.BuildServiceProvider();
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);
        public HttpClient CreateClient(string name) => _client;
        public void Dispose() => _client.Dispose();
    }

    private sealed class ResponseHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(Interlocked.Increment(ref _calls)));
    }

    private sealed class TrackedResponse : HttpResponseMessage
    {
        public TrackedResponse(HttpStatusCode status, HttpContent content) : base(status)
        {
            Content = content;
            Headers.Add("x-request-id", "provider-id");
        }
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ForwardOnlyStream(byte[] bytes, int segmentSize = 17, bool stallAfterData = false) : Stream
    {
        private int _position;
        public int BytesRead => _position;
        public bool Disposed { get; private set; }
        public TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAvailable(buffer.AsSpan(offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position == bytes.Length && stallAfterData) return new(WaitForCancellation(cancellationToken));
            return ValueTask.FromResult(ReadAvailable(buffer.Span));
        }
        private int ReadAvailable(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, segmentSize), bytes.Length - _position);
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        private async Task<int> WaitForCancellation(CancellationToken token)
        {
            Stalled.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
