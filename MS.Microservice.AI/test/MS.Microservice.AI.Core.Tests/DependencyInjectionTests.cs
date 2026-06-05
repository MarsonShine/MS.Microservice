using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

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
}