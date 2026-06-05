using FluentAssertions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIOptionsValidatorTests
{
    [Fact]
    public void Validate_ShouldReturnFailures_WhenDefaultProviderDoesNotExist()
    {
        var options = new AIOptions
        {
            DefaultProvider = "MissingProvider",
        };

        var result = new AIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle(failure => failure.Contains("DefaultProvider", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldReturnFailures_WhenModelProviderIsMissing()
    {
        var options = new AIOptions();
        options.Models.Chat.Add("Default", new AIChatModelOptions
        {
            Provider = "Qwen",
            Model = "qwen-plus",
        });

        var result = new AIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("AI:Models:Chat:Default:Provider 'Qwen'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldReturnFailures_WhenProviderLimitsAreInvalid()
    {
        var options = new AIOptions();
        options.Providers.Add("OpenAI", new AIProviderRegistrationOptions
        {
            TimeoutSeconds = 0,
            MaxRetryAttempts = -1,
            ConcurrencyLimit = 0,
        });

        var result = new AIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }
}