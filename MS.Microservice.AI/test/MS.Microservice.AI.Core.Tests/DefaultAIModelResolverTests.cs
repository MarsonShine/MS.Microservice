using FluentAssertions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class DefaultAIModelResolverTests
{
    [Fact]
    public void ResolveChatModel_ShouldUseScenarioConfiguration_WhenScenarioExists()
    {
        var options = CreateOptions();
        options.Models.Chat.Add("CustomerSupport", new AIChatModelOptions
        {
            Provider = "DeepSeek",
            Model = "deepseek-v4-pro",
            Temperature = 0.4,
            TimeoutSeconds = 45,
            MaxRetryAttempts = 1,
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        var resolved = resolver.ResolveChatModel(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            Scenario = "CustomerSupport",
        });

        resolved.Provider.Should().Be("DeepSeek");
        resolved.Model.Should().Be("deepseek-v4-pro");
        resolved.Scenario.Should().Be("CustomerSupport");
        resolved.Temperature.Should().Be(0.4);
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(45));
        resolved.MaxRetryAttempts.Should().Be(1);
    }

    [Fact]
    public void ResolveChatModel_ShouldPreferExplicitOverrides_WhenProvided()
    {
        var resolver = new DefaultAIModelResolver(Options.Create(CreateOptions()));

        var resolved = resolver.ResolveChatModel(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            Provider = "Qwen",
            Model = "qwen-plus",
            Temperature = 0.8,
            MaxOutputTokens = 128,
            Timeout = TimeSpan.FromSeconds(30),
        });

        resolved.Provider.Should().Be("Qwen");
        resolved.Model.Should().Be("qwen-plus");
        resolved.Temperature.Should().Be(0.8);
        resolved.MaxOutputTokens.Should().Be(128);
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        resolved.MaxRetryAttempts.Should().Be(2);
    }

    [Fact]
    public void ResolveChatModel_ShouldThrow_WhenProviderOverrideHasNoModel()
    {
        var resolver = new DefaultAIModelResolver(Options.Create(CreateOptions()));

        Action action = () => resolver.ResolveChatModel(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            Provider = "OpenAI",
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*must specify a model when provider override is used*");
    }

    [Fact]
    public void ResolveChatModel_ShouldThrow_WhenNoScenarioAndNoDefaultExist()
    {
        var options = CreateOptions();
        options.Models.Chat.Clear();
        var resolver = new DefaultAIModelResolver(Options.Create(options));

        Action action = () => resolver.ResolveChatModel(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*no chat scenario or default model is configured*");
    }

    [Fact]
    public void ResolveTtsModel_ShouldUseScenarioDefaults_AndRequestOverrides()
    {
        var options = CreateOptions();
        options.Models.Tts.Add("Speech", new AITtsModelOptions
        {
            Provider = "OpenAI",
            Model = "gpt-4o-mini-tts",
            Voice = "alloy",
            ResponseFormat = "wav",
            Speed = 1.1,
            TimeoutSeconds = 15,
            MaxRetryAttempts = 1,
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        var resolved = resolver.ResolveTtsModel(new AITtsRequest
        {
            Input = "hello",
            Scenario = "Speech",
            Speed = 1.3,
        });

        resolved.Capability.Should().Be(AICapability.Tts);
        resolved.Provider.Should().Be("OpenAI");
        resolved.Model.Should().Be("gpt-4o-mini-tts");
        resolved.Voice.Should().Be("alloy");
        resolved.ResponseFormat.Should().Be("wav");
        resolved.Speed.Should().Be(1.3);
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ResolveAsrModel_ShouldPreferExplicitOverrides_WhenProvided()
    {
        var resolver = new DefaultAIModelResolver(Options.Create(CreateOptions()));

        var resolved = resolver.ResolveAsrModel(new AIAsrRequest
        {
            Audio = new AIBinaryContent { Content = [1, 2, 3], FileName = "audio.wav", ContentType = "audio/wav" },
            Provider = "OpenAI",
            Model = "whisper-1",
            Language = "zh",
            ResponseFormat = "json",
            Timeout = TimeSpan.FromSeconds(12),
        });

        resolved.Capability.Should().Be(AICapability.Asr);
        resolved.Provider.Should().Be("OpenAI");
        resolved.Model.Should().Be("whisper-1");
        resolved.Language.Should().Be("zh");
        resolved.ResponseFormat.Should().Be("json");
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void ResolveImageGenerationModel_ShouldPreferExplicitCount_AndRetainConfiguredDefaults()
    {
        var options = CreateOptions();
        options.Models.ImageGeneration.Add("Default", new AIImageModelOptions
        {
            Provider = "Qwen",
            Model = "qwen-image",
            Count = 2,
            Size = "1024x1024",
            ResponseFormat = "b64_json",
            TimeoutSeconds = 40,
            MaxRetryAttempts = 4,
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        var resolved = resolver.ResolveImageGenerationModel(new AIImageGenerationRequest
        {
            Prompt = "draw a cat",
            Count = 3,
            Quality = "hd",
        });

        resolved.Capability.Should().Be(AICapability.ImageGeneration);
        resolved.Provider.Should().Be("Qwen");
        resolved.Model.Should().Be("qwen-image");
        resolved.Count.Should().Be(3);
        resolved.Size.Should().Be("1024x1024");
        resolved.Quality.Should().Be("hd");
        resolved.ResponseFormat.Should().Be("b64_json");
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(40));
        resolved.MaxRetryAttempts.Should().Be(4);
    }

    [Fact]
    public void ResolveTtsModel_WhenOnlyExplicitModelIsProvided_ShouldFallbackToDefaultProviderSettings()
    {
        var resolver = new DefaultAIModelResolver(Options.Create(CreateOptions()));

        var resolved = resolver.ResolveTtsModel(new AITtsRequest
        {
            Input = "hello",
            Model = "gpt-4o-mini-tts",
        });

        resolved.Provider.Should().Be("OpenAI");
        resolved.Model.Should().Be("gpt-4o-mini-tts");
        resolved.Scenario.Should().Be("Default");
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(100));
        resolved.MaxRetryAttempts.Should().Be(2);
    }

    [Fact]
    public void ResolveTtsModel_WhenProviderOverrideHasNoModel_ShouldThrowConfigurationException()
    {
        var resolver = new DefaultAIModelResolver(Options.Create(CreateOptions()));

        Action action = () => resolver.ResolveTtsModel(new AITtsRequest
        {
            Input = "hello",
            Provider = "OpenAI",
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*must specify a model when provider override is used*");
    }

    [Fact]
    public void ResolveAsrModel_WhenNoScenarioAndNoDefaultConfigured_ShouldThrowConfigurationException()
    {
        var options = CreateOptions();
        options.Models.Asr.Clear();
        var resolver = new DefaultAIModelResolver(Options.Create(options));

        Action action = () => resolver.ResolveAsrModel(new AIAsrRequest
        {
            Audio = new AIBinaryContent
            {
                Content = [1, 2, 3],
                FileName = "sample.wav",
                ContentType = "audio/wav",
            },
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*no scenario-specific or default model is configured*");
    }

    [Fact]
    public void ResolveImageEditModel_WhenScenarioMatchesCaseInsensitive_ShouldUseScenarioConfiguration()
    {
        var options = CreateOptions();
        options.Models.ImageEdit.Add("Marketing", new AIImageModelOptions
        {
            Provider = "Qwen",
            Model = "qwen-image-edit",
            Count = 2,
            Size = "512x512",
            Quality = "hd",
            ResponseFormat = "url",
            TimeoutSeconds = 55,
            MaxRetryAttempts = 4,
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        var resolved = resolver.ResolveImageEditModel(new AIImageEditRequest
        {
            Scenario = "marketing",
            Prompt = "clean up background",
            Image = new AIBinaryContent
            {
                Content = [1, 2, 3],
                FileName = "image.png",
                ContentType = "image/png",
            },
        });

        resolved.Provider.Should().Be("Qwen");
        resolved.Model.Should().Be("qwen-image-edit");
        resolved.Scenario.Should().Be("Marketing");
        resolved.Count.Should().Be(2);
        resolved.Size.Should().Be("512x512");
        resolved.Quality.Should().Be("hd");
        resolved.ResponseFormat.Should().Be("url");
        resolved.Timeout.Should().Be(TimeSpan.FromSeconds(55));
        resolved.MaxRetryAttempts.Should().Be(4);
    }

    [Fact]
    public void ResolveImageGenerationModel_WhenProviderCannotBeDetermined_ShouldThrowConfigurationException()
    {
        var options = CreateOptions();
        options.DefaultProvider = null;
        options.Models.ImageGeneration.Add("Default", new AIImageModelOptions
        {
            Provider = null!,
            Model = "gpt-image-1",
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        Action action = () => resolver.ResolveImageGenerationModel(new AIImageGenerationRequest
        {
            Prompt = "draw a cat",
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*no provider could be determined*");
    }

    [Fact]
    public void ResolveImageGenerationModel_WhenResolvedProviderIsNotConfigured_ShouldThrowConfigurationException()
    {
        var options = CreateOptions();
        options.Models.ImageGeneration.Add("Default", new AIImageModelOptions
        {
            Provider = "Missing",
            Model = "gpt-image-1",
        });

        var resolver = new DefaultAIModelResolver(Options.Create(options));

        Action action = () => resolver.ResolveImageGenerationModel(new AIImageGenerationRequest
        {
            Prompt = "draw a cat",
        });

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*provider 'Missing' is not configured*");
    }

    private static AIOptions CreateOptions()
    {
        var options = new AIOptions
        {
            DefaultProvider = "OpenAI",
        };

        options.Providers.Add("OpenAI", new AIProviderRegistrationOptions
        {
            BaseAddress = "https://api.openai.com/v1/",
            ApiKey = "openai-key",
            TimeoutSeconds = 100,
            MaxRetryAttempts = 2,
            ConcurrencyLimit = 8,
        });
        options.Providers.Add("DeepSeek", new AIProviderRegistrationOptions
        {
            BaseAddress = "https://api.deepseek.com/",
            ApiKey = "deepseek-key",
            TimeoutSeconds = 80,
            MaxRetryAttempts = 3,
            ConcurrencyLimit = 4,
        });
        options.Providers.Add("Qwen", new AIProviderRegistrationOptions
        {
            BaseAddress = "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            ApiKey = "qwen-key",
            TimeoutSeconds = 120,
            MaxRetryAttempts = 2,
            ConcurrencyLimit = 3,
        });
        options.Models.Chat.Add("Default", new AIChatModelOptions
        {
            Provider = "OpenAI",
            Model = "gpt-4.1-mini",
            Temperature = 0.2,
        });

        return options;
    }
}
