using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddMicroserviceAI_ShouldRegisterRoutingClient_AndProviderPipeline()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:DefaultProvider"] = "OpenAI",
                ["AI:Providers:OpenAI:ApiKey"] = "test-key",
                ["AI:Providers:OpenAI:BaseAddress"] = "https://api.openai.com/v1/",
                ["AI:Providers:OpenAI:TimeoutSeconds"] = "10",
                ["AI:Providers:OpenAI:MaxRetryAttempts"] = "1",
                ["AI:Providers:OpenAI:ConcurrencyLimit"] = "2",
                ["AI:Models:Chat:Default:Provider"] = "OpenAI",
                ["AI:Models:Chat:Default:Model"] = "gpt-4.1-mini",
                ["AI:Models:Tts:Default:Provider"] = "OpenAI",
                ["AI:Models:Tts:Default:Model"] = "gpt-4o-mini-tts",
                ["AI:Models:Tts:Default:Voice"] = "alloy",
                ["AI:Models:Asr:Default:Provider"] = "OpenAI",
                ["AI:Models:Asr:Default:Model"] = "whisper-1",
                ["AI:Models:ImageGeneration:Default:Provider"] = "OpenAI",
                ["AI:Models:ImageGeneration:Default:Model"] = "gpt-image-1",
                ["AI:Models:ImageEdit:Default:Provider"] = "OpenAI",
                ["AI:Models:ImageEdit:Default:Model"] = "gpt-image-1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration)
            .AddOpenAI();

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IAIChatClient>();
        var ttsClient = provider.GetRequiredService<IAITtsClient>();
        var asrClient = provider.GetRequiredService<IAIAsrClient>();
        var imageGenerationClient = provider.GetRequiredService<IAIImageGenerationClient>();
        var imageEditClient = provider.GetRequiredService<IAIImageEditClient>();
        var chatProviders = provider.GetRequiredService<IEnumerable<IAIChatProvider>>();
        var ttsProviders = provider.GetRequiredService<IEnumerable<IAITtsProvider>>();
        var asrProviders = provider.GetRequiredService<IEnumerable<IAIAsrProvider>>();
        var imageGenerationProviders = provider.GetRequiredService<IEnumerable<IAIImageGenerationProvider>>();
        var imageEditProviders = provider.GetRequiredService<IEnumerable<IAIImageEditProvider>>();
        var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;

        chatClient.Should().BeOfType<RoutingAIChatClient>();
        ttsClient.Should().BeOfType<RoutingAITtsClient>();
        asrClient.Should().BeOfType<RoutingAIAsrClient>();
        imageGenerationClient.Should().BeOfType<RoutingAIImageGenerationClient>();
        imageEditClient.Should().BeOfType<RoutingAIImageEditClient>();
        chatProviders.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        ttsProviders.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        asrProviders.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        imageGenerationProviders.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        imageEditProviders.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        options.DefaultProvider.Should().Be("OpenAI");
        options.Models.Chat.Should().ContainKey("Default");
        options.Models.Tts.Should().ContainKey("Default");
        options.Models.Asr.Should().ContainKey("Default");
        options.Models.ImageGeneration.Should().ContainKey("Default");
        options.Models.ImageEdit.Should().ContainKey("Default");
    }

    [Fact]
    public void AddImagePromptPipeline_ShouldRegisterPlanGeneratorClient_AsSingleton()
    {
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(new FakeChatClient());

        services.AddImagePromptPipeline();

        using var provider = services.BuildServiceProvider();
        var client1 = provider.GetRequiredService<IPlanGeneratorClient>();
        var client2 = provider.GetRequiredService<IPlanGeneratorClient>();

        client1.Should().BeOfType<PlanGeneratorClient>();
        client1.Should().BeSameAs(client2); // Singleton
    }

    [Fact]
    public void AddImagePromptPipeline_ShouldRegisterWordImagePromptPipeline_AsTransient()
    {
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(new FakeChatClient());

        services.AddImagePromptPipeline();

        using var provider = services.BuildServiceProvider();
        var pipeline1 = provider.GetRequiredService<WordImagePromptPipeline>();
        var pipeline2 = provider.GetRequiredService<WordImagePromptPipeline>();

        pipeline1.Should().NotBeNull();
        pipeline1.Should().NotBeSameAs(pipeline2); // Transient
    }

    [Fact]
    public async Task AddImagePromptPipeline_ShouldUseCustomScenario_WhenProvided()
    {
        var fakeChat = new FakeChatClient();
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(fakeChat);

        services.AddImagePromptPipeline("MyPromptScenario");

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPlanGeneratorClient>();

        var input = new WordImageInput("A", "A", "letter A", WordImageCardType.Alphabet);
        await client.GenerateAlphabetPlanAsync(input);

        fakeChat.LastScenario.Should().Be("MyPromptScenario");
        fakeChat.LastModel.Should().BeNull();
    }

    [Fact]
    public async Task AddImagePromptPipeline_ShouldUseDefaultScenario_WhenNullProvided()
    {
        var fakeChat = new FakeChatClient();
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(fakeChat);

        services.AddImagePromptPipeline(scenario: null!);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPlanGeneratorClient>();

        var input = new WordImageInput("A", "A", "letter A", WordImageCardType.Alphabet);
        await client.GenerateAlphabetPlanAsync(input);

        fakeChat.LastScenario.Should().Be(PlanGeneratorClient.DefaultScenario);
    }

    [Fact]
    public void AddImagePromptPipeline_ShouldRegisterSceneGroupingAgent_AsSingleton()
    {
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(new FakeChatClient());

        services.AddImagePromptPipeline();

        using var provider = services.BuildServiceProvider();
        var agent1 = provider.GetRequiredService<ISceneGroupingAgent>();
        var agent2 = provider.GetRequiredService<ISceneGroupingAgent>();

        agent1.Should().BeOfType<SceneGroupingAgent>();
        agent1.Should().BeSameAs(agent2); // Singleton
    }

    [Fact]
    public void AddImagePromptPipeline_ShouldRegisterImageGenerationOrchestrator_AsTransient()
    {
        var services = new ServiceCollection();
        RegisterLoggerStub(services);
        services.AddSingleton<IAIChatClient>(new FakeChatClient());
        services.AddSingleton<IAIImageGenerationClient>(new FakeImageGenerationClient());

        services.AddImagePromptPipeline();

        using var provider = services.BuildServiceProvider();
        var orch1 = provider.GetRequiredService<ImageGenerationOrchestrator>();
        var orch2 = provider.GetRequiredService<ImageGenerationOrchestrator>();

        orch1.Should().NotBeNull();
        orch1.Should().NotBeSameAs(orch2); // Transient
    }

    // ── Test doubles ──

    private static void RegisterLoggerStub(IServiceCollection services)
    {
        services.AddSingleton<ILogger<PlanGeneratorClient>>(NullLogger<PlanGeneratorClient>.Instance);
        services.AddSingleton<ILogger<WordImagePromptPipeline>>(NullLogger<WordImagePromptPipeline>.Instance);
        services.AddSingleton<ILogger<ImageGenerationOrchestrator>>(NullLogger<ImageGenerationOrchestrator>.Instance);
        services.AddSingleton<ILogger<SceneGroupingAgent>>(NullLogger<SceneGroupingAgent>.Instance);
    }

    private sealed class FakeChatClient : IAIChatClient
    {
        public string? LastModel { get; private set; }
        public string? LastScenario { get; private set; }

        public ValueTask<AIChatResponse> GetResponseAsync(
            AIChatRequest request, CancellationToken cancellationToken = default)
        {
            LastModel = request.Model;
            LastScenario = request.Scenario;
            return ValueTask.FromResult(new AIChatResponse
            {
                Provider = "fake",
                Model = "fake",
                Text = "<Output>{\"mainSubject\":\"test\"}</Output>"
            });
        }

        public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(
            AIChatRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeImageGenerationClient : IAIImageGenerationClient
    {
        public ValueTask<AIImageResponse> GenerateAsync(
            AIImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = "fake",
                Model = "fake",
                Images = [new AIImageData { Url = "https://fake.url/image.png" }]
            });
        }

        public ValueTask<AIImageResponse> EditAsync(
            AIImageEditRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}