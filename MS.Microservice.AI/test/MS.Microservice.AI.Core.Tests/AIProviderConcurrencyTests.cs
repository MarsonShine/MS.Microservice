using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.Threading;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIProviderConcurrencyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ProviderNamesShareSlotsIgnoringCaseAndDifferentProvidersRemainIndependent()
    {
        var options = CreateOptions();
        options.Providers["other"] = new() { ConcurrencyLimit = 1 };
        var concurrency = new AIProviderConcurrency(Options.Create(options));
        using var deadline = new CancellationTokenSource(TestTimeout);
        using var held = await concurrency.GetRequired("OPENAI").AcquireAsync(deadline.Token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var waiting = concurrency.GetRequired("openai").AcquireAsync(canceled.Token).AsTask();
        using var independent = await concurrency.GetRequired("other").AcquireAsync(deadline.Token);
        Assert.False(waiting.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Theory]
    [InlineData("chat", "tts")]
    [InlineData("tts", "asr")]
    [InlineData("image", "edit")]
    public async Task CrossCapabilityRequestsShareProviderLimit(string firstCapability, string secondCapability)
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        using var handler = new RecordingHandler(blockFirst: true);
        using var container = CreateContainer(handler);
        var first = Invoke(container, firstCapability, deadline.Token);
        await handler.Started.Task.WaitAsync(deadline.Token);
        var second = Invoke(container, secondCapability, deadline.Token);
        try
        {
            Assert.Equal(1, handler.Calls);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            handler.Release.TrySetResult();
            await Task.WhenAll(first, second);
        }
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task CanceledCrossCapabilityWaiterDoesNotReleaseTheActiveRequest()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        using var handler = new RecordingHandler(blockFirst: true);
        using var container = CreateContainer(handler);
        var first = Invoke(container, "chat", deadline.Token);
        await handler.Started.Task.WaitAsync(deadline.Token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var waiting = Invoke(container, "tts", canceled.Token);
        Task? next = null;
        try
        {
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            next = Invoke(container, "asr", deadline.Token);
            Assert.Equal(1, handler.Calls);
            Assert.False(next.IsCompleted);
        }
        finally
        {
            handler.Release.TrySetResult();
            await first;
            if (next is not null) await next;
        }
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task StreamingChatHoldsTheSharedSlotUntilItsEnumeratorIsDisposed()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        using var handler = new RecordingHandler(streamChat: true);
        using var container = CreateContainer(handler);
        await using var stream = container.GetRequiredService<IAIChatClient>()
            .StreamAsync(ChatRequest(), deadline.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var waiting = Invoke(container, "tts", deadline.Token);
        try
        {
            Assert.Equal(1, handler.Calls);
            Assert.False(waiting.IsCompleted);
        }
        finally
        {
            await stream.DisposeAsync();
            await waiting;
        }
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ContainersDoNotShareSlotsEvenWhenTheyShareTheSameOptionsInstance()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        var options = Options.Create(CreateOptions());
        using var firstHandler = new RecordingHandler(blockFirst: true);
        using var secondHandler = new RecordingHandler(blockFirst: true);
        using var firstContainer = CreateContainer(firstHandler, options);
        using var secondContainer = CreateContainer(secondHandler, options);
        var first = Invoke(firstContainer, "chat", deadline.Token);
        await firstHandler.Started.Task.WaitAsync(deadline.Token);
        var second = Invoke(secondContainer, "chat", deadline.Token);
        try
        {
            await secondHandler.Started.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, firstHandler.Calls);
            Assert.Equal(1, secondHandler.Calls);
        }
        finally
        {
            firstHandler.Release.TrySetResult();
            secondHandler.Release.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task UnusedDisabledConfigurationDoesNotCreateAnAdditionalGate()
    {
        var options = CreateOptions();
        options.Providers["unused"] = new() { Enabled = false, ConcurrencyLimit = 0 };
        using var handler = new RecordingHandler();
        // Supplying IOptions directly preserves the existing caller-owned validation contract.
        using var container = CreateContainer(handler, Options.Create(options));
        using var deadline = new CancellationTokenSource(TestTimeout);
        await Invoke(container, "chat", deadline.Token);
        Assert.Equal(1, handler.Calls);
    }

    private static ServiceProvider CreateContainer(HttpMessageHandler handler, IOptions<AIOptions>? options = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Providers:oPeNaI:ApiKey"] = "test-key",
            ["AI:Providers:oPeNaI:ConcurrencyLimit"] = "1",
            ["AI:Providers:oPeNaI:MaxRetryAttempts"] = "0"
        }).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddOpenAI();
        if (options is not null) services.AddSingleton(options);
        services.AddSingleton<IHttpClientFactory>(_ => new ClientFactory(handler));
        return services.BuildServiceProvider();
    }

    private static AIOptions CreateOptions()
    {
        var options = new AIOptions();
        options.Providers["oPeNaI"] = new() { ApiKey = "test-key", ConcurrencyLimit = 1, MaxRetryAttempts = 0 };
        return options;
    }

    private static AIChatRequest ChatRequest() => new()
    {
        Provider = "openai", Model = "test", Messages = [new("user", "hello")]
    };

    private static Task Invoke(IServiceProvider services, string capability, CancellationToken token) => capability switch
    {
        "chat" => services.GetRequiredService<IAIChatClient>().GetResponseAsync(ChatRequest(), token).AsTask(),
        "tts" => services.GetRequiredService<IAITtsClient>().SynthesizeAsync(new()
        {
            Provider = "OPENAI", Model = "test", Voice = "alloy", Input = "hello"
        }, token).AsTask(),
        "asr" => services.GetRequiredService<IAIAsrClient>().RecognizeAsync(new()
        {
            Provider = "openai", Model = "test", Audio = new() { Content = [1], FileName = "audio.wav" }
        }, token).AsTask(),
        "image" => services.GetRequiredService<IAIImageGenerationClient>().GenerateAsync(new()
        {
            Provider = "openai", Model = "test", Prompt = "image"
        }, token).AsTask(),
        "edit" => services.GetRequiredService<IAIImageEditClient>().EditAsync(new()
        {
            Provider = "openai", Model = "test", Prompt = "image", Image = new() { Content = [1], FileName = "image.png" }
        }, token).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);
        public HttpClient CreateClient(string name) => _client;
        public void Dispose() => _client.Dispose();
    }

    private sealed class RecordingHandler(bool blockFirst = false, bool streamChat = false) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var call = Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            if (blockFirst && call == 1) await Release.Task.WaitAsync(token);
            HttpContent content = request.RequestUri!.AbsolutePath switch
            {
                "/v1/audio/speech" => new ByteArrayContent([1]),
                "/v1/audio/transcriptions" => new StringContent("""{"text":"hello"}"""),
                "/v1/images/generations" or "/v1/images/edits" => new StringContent("""{"data":[{"url":"https://example.invalid/image.png"}]}"""),
                _ when streamChat => new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\ndata: [DONE]\n\n"),
                _ => new StringContent("""{"choices":[{"message":{"content":"hello"},"finish_reason":"stop"}]}""")
            };
            return new(HttpStatusCode.OK) { Content = content };
        }
    }
}
