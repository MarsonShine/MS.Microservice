using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIProductionReadinessTests
{
    [Fact]
    public void AddMicroserviceAI_ShouldRegisterProductionReadinessHooks()
    {
        var services = new ServiceCollection();
        services.AddMicroserviceAI(CreateConfiguration());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAIRateLimiter>().Should().NotBeNull();
        provider.GetRequiredService<IAICircuitBreaker>().Should().NotBeNull();
        provider.GetRequiredService<IAILogSanitizer>().Should().NotBeNull();
        provider.GetRequiredService<IAISecretProvider>().Should().NotBeNull();
        provider.GetRequiredService<IAICostReporter>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<AIPayloadLimitOptions>>().Value.MaxAudioBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DefaultAILogSanitizer_ShouldRedactApiKeysBearerTokensAndSensitiveFields()
    {
        var services = new ServiceCollection();
        services.AddAILogSanitizer();

        using var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<IAILogSanitizer>();

        var sanitized = sanitizer.Sanitize("Authorization: Bearer sk-secret api_key=abc prompt=hello response=world");

        sanitized.Should().NotContain("sk-secret");
        sanitized.Should().NotContain("abc");
        sanitized.Should().NotContain("hello");
        sanitized.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task DefaultAIRateLimiter_ShouldRejectRequestsBeyondConfiguredWindow()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:RequestsPerWindow"] = "1",
                ["RateLimiting:WindowSeconds"] = "60",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddAIRateLimiter(configuration.GetSection("RateLimiting"));
        using var provider = services.BuildServiceProvider();
        var limiter = provider.GetRequiredService<IAIRateLimiter>();
        var context = CreateContext();

        await using var lease = await limiter.AcquireAsync(context);

        Func<Task> action = async () => await limiter.AcquireAsync(context);
        await action.Should().ThrowAsync<AIRateLimitException>();
    }

    [Fact]
    public async Task DefaultAICircuitBreaker_ShouldOpenAfterFailureThreshold()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CircuitBreaker:Enabled"] = "true",
                ["CircuitBreaker:FailureThreshold"] = "1",
                ["CircuitBreaker:BreakDurationSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddAICircuitBreaker(configuration.GetSection("CircuitBreaker"));
        using var provider = services.BuildServiceProvider();
        var circuitBreaker = provider.GetRequiredService<IAICircuitBreaker>();
        var context = CreateContext();

        await circuitBreaker.RecordFailureAsync(context, new AIProviderException("down", AIErrorCodes.ProviderUnavailable));

        Func<Task> action = async () => await circuitBreaker.EnsureAllowedAsync(context);
        await action.Should().ThrowAsync<AIProviderException>().Where(exception => exception.ErrorCode == AIErrorCodes.CircuitOpen);
    }

    [Fact]
    public void SecretProvider_ShouldPopulateApiKeyFromEnvironmentSecretName()
    {
        var secretName = $"MS_AI_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(secretName, "env-secret");

        try
        {
            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["AI:Providers:OpenAI:ApiKey"] = null,
                ["AI:Providers:OpenAI:ApiKeySecretName"] = secretName,
            });
            var services = new ServiceCollection();
            services.AddMicroserviceAI(configuration);

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;

            options.Providers["OpenAI"].ApiKey.Should().Be("env-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, null);
        }
    }

    [Fact]
    public async Task RoutingClient_ShouldApplyPayloadLimits()
    {
        var client = new RoutingAIChatClient(
            new FakeModelResolver(),
            new FakeProviderFactory(new FakeChatProvider()),
            Options.Create(new AIPayloadLimitOptions { MaxChatCharacters = 2 }));

        Func<Task> action = async () => await client.GetResponseAsync(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
        });

        await action.Should().ThrowAsync<AIConfigurationException>().WithMessage("*configured limit*");
    }

    [Fact]
    public async Task RoutingClient_ShouldReportCostRecord_WhenRequestSucceeds()
    {
        var reporter = new RecordingCostReporter();
        var services = new ServiceCollection();
        services.AddSingleton<IAICostReporter>(reporter);
        services.AddMicroserviceAI(CreateConfiguration());
        services.AddSingleton<IAIChatProvider>(new FakeChatProvider());

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IAIChatClient>();

        await client.GetResponseAsync(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
        });

        reporter.Records.Should().ContainSingle();
        var record = reporter.Records[0];
        record.Provider.Should().Be("OpenAI");
        record.Model.Should().Be("gpt-test");
        record.InputTokens.Should().Be(3);
        record.OutputTokens.Should().Be(4);
        record.TotalTokens.Should().Be(7);
        record.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ProductionOptionValidators_WhenOptionsAreInvalid_ShouldReturnExpectedFailures()
    {
        var invalidLogSanitizer = new AILogSanitizerOptions
        {
            RedactionText = string.Empty,
        };
        invalidLogSanitizer.SensitiveFields.Clear();
        invalidLogSanitizer.SensitiveFields.Add(string.Empty);

        var rateResult = new AIRateLimitingOptionsValidator().Validate(null, new AIRateLimitingOptions
        {
            RequestsPerWindow = 0,
            WindowSeconds = 0,
        });
        var circuitResult = new AICircuitBreakerOptionsValidator().Validate(null, new AICircuitBreakerOptions
        {
            FailureThreshold = 0,
            BreakDurationSeconds = 0,
        });
        var logResult = new AILogSanitizerOptionsValidator().Validate(null, invalidLogSanitizer);
        var secretResult = new AISecretProviderOptionsValidator().Validate(null, new AISecretProviderOptions
        {
            EnvironmentVariablePrefix = string.Empty,
        });
        var payloadResult = new AIPayloadLimitOptionsValidator().Validate(null, new AIPayloadLimitOptions
        {
            MaxChatCharacters = 0,
            MaxStreamingChatCharacters = 0,
            MaxTextCharacters = 0,
            MaxAudioBytes = 0,
            MaxImagePromptCharacters = 0,
            MaxImageBytes = 0,
            MaxImageMaskBytes = 0,
        });

        rateResult.Failures.Should().HaveCount(2);
        circuitResult.Failures.Should().HaveCount(2);
        logResult.Failures.Should().HaveCount(2);
        secretResult.Failures.Should().ContainSingle("AI:SecretProvider:EnvironmentVariablePrefix is required.");
        payloadResult.Failures.Should().HaveCount(7);
    }

    [Fact]
    public void ProductionOptionValidators_WhenOptionsAreValid_ShouldSucceed()
    {
        var rateResult = new AIRateLimitingOptionsValidator().Validate(null, new AIRateLimitingOptions
        {
            RequestsPerWindow = 1,
            WindowSeconds = 30,
        });
        var circuitResult = new AICircuitBreakerOptionsValidator().Validate(null, new AICircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDurationSeconds = 30,
        });
        var logResult = new AILogSanitizerOptionsValidator().Validate(null, new AILogSanitizerOptions());
        var secretResult = new AISecretProviderOptionsValidator().Validate(null, new AISecretProviderOptions
        {
            EnvironmentVariablePrefix = "AI__PROVIDERS__",
        });
        var payloadResult = new AIPayloadLimitOptionsValidator().Validate(null, new AIPayloadLimitOptions());

        rateResult.Succeeded.Should().BeTrue();
        circuitResult.Succeeded.Should().BeTrue();
        logResult.Succeeded.Should().BeTrue();
        secretResult.Succeeded.Should().BeTrue();
        payloadResult.Succeeded.Should().BeTrue();
    }

    private static IConfigurationRoot CreateConfiguration(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:DefaultProvider"] = "OpenAI",
            ["AI:Providers:OpenAI:ApiKey"] = "test-key",
            ["AI:Providers:OpenAI:TimeoutSeconds"] = "10",
            ["AI:Providers:OpenAI:MaxRetryAttempts"] = "1",
            ["AI:Providers:OpenAI:ConcurrencyLimit"] = "2",
            ["AI:Models:Chat:Default:Provider"] = "OpenAI",
            ["AI:Models:Chat:Default:Model"] = "gpt-test",
        };

        if (overrides is not null)
        {
            foreach (var item in overrides)
            {
                values[item.Key] = item.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static AIRequestContext CreateContext()
    {
        return new AIRequestContext
        {
            Provider = "OpenAI",
            Model = "gpt-test",
            Capability = AICapability.Chat,
            Scenario = "Default",
            RequestId = "request-1",
        };
    }

    private sealed class FakeModelResolver : IAIModelResolver
    {
        private static readonly AIResolvedModel Model = new()
        {
            Provider = "OpenAI",
            Model = "gpt-test",
            Scenario = "Default",
            Capability = AICapability.Chat,
            Timeout = TimeSpan.FromSeconds(10),
        };

        public AIResolvedModel ResolveChatModel(AIChatRequest request) => Model;
        public AIResolvedModel ResolveTtsModel(AITtsRequest request) => Model;
        public AIResolvedModel ResolveAsrModel(AIAsrRequest request) => Model;
        public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request) => Model;
        public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request) => Model;
    }

    private sealed class FakeProviderFactory(IAIChatProvider provider) : IAIProviderFactory
    {
        public IAIChatProvider GetRequiredChatProvider(string providerName) => provider;
        public IAITtsProvider GetRequiredTtsProvider(string providerName) => throw new NotSupportedException();
        public IAIAsrProvider GetRequiredAsrProvider(string providerName) => throw new NotSupportedException();
        public IAIImageGenerationProvider GetRequiredImageGenerationProvider(string providerName) => throw new NotSupportedException();
        public IAIImageEditProvider GetRequiredImageEditProvider(string providerName) => throw new NotSupportedException();
    }

    private sealed class FakeChatProvider : IAIChatProvider
    {
        public string Name => "OpenAI";

        public ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AIChatResponse
            {
                Provider = Name,
                Model = model.Model,
                Text = "ok",
                Usage = new AIUsage { InputTokens = 3, OutputTokens = 4, TotalTokens = 7 },
            });
        }

        public async IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AIChatStreamChunk { IsFinal = true, Usage = new AIUsage { InputTokens = 1, OutputTokens = 1, TotalTokens = 2 } };
        }
    }

    private sealed class RecordingCostReporter : IAICostReporter
    {
        public List<AICostRecord> Records { get; } = [];

        public ValueTask ReportAsync(AICostRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
