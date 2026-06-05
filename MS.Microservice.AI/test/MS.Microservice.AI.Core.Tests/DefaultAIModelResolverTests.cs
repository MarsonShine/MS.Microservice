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