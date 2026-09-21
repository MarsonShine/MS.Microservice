using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MS.Microservice.AI.Core.Tests;

public sealed class GeneratedConfigurationBindingTests
{
    [Fact]
    public void BindingPreservesNestedDictionariesDefaultsAndAllProductionOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:ApiKey"] = "key",
            ["AI:Providers:Test:Endpoints:Custom"] = "https://example.test/",
            ["AI:Providers:Test:Headers:X-Test"] = "header",
            ["AI:Models:Chat:Default:Provider"] = "Test",
            ["AI:Models:Chat:Default:Model"] = "model",
            ["AI:RateLimiting:RequestsPerWindow"] = "3",
            ["AI:CircuitBreaker:FailureThreshold"] = "7",
            ["AI:LogSanitizer:SensitiveFields:0"] = "CUSTOM",
            ["AI:SecretProvider:PreferEnvironment"] = "false",
            ["AI:PayloadLimits:MaxAudioBytes"] = "512",
            ["AI:CostAccounting:Enabled"] = "false",
        }).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;
        Assert.Equal("https://example.test/", options.Providers["test"].Endpoints["custom"]);
        Assert.Equal("header", options.Providers["TEST"].Headers["x-test"]);
        Assert.Equal("model", options.Models.Chat["default"].Model);
        Assert.Equal(100, options.Providers["test"].TimeoutSeconds);
        Assert.Equal(3, provider.GetRequiredService<IOptions<AIRateLimitingOptions>>().Value.RequestsPerWindow);
        Assert.Equal(7, provider.GetRequiredService<IOptions<AICircuitBreakerOptions>>().Value.FailureThreshold);
        var sensitive = provider.GetRequiredService<IOptions<AILogSanitizerOptions>>().Value.SensitiveFields;
        Assert.True(sensitive.Contains("custom"));
        Assert.True(sensitive.Contains("API_KEY"));
        Assert.False(provider.GetRequiredService<IOptions<AISecretProviderOptions>>().Value.PreferEnvironment);
        Assert.Equal(512, provider.GetRequiredService<IOptions<AIPayloadLimitOptions>>().Value.MaxAudioBytes);
        Assert.False(provider.GetRequiredService<IOptions<AICostAccountingOptions>>().Value.Enabled);
    }

    [Fact]
    public void ConcreteBindingRetainsReloadAndValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RequestsPerWindow"] = "3",
        }).Build();
        var services = new ServiceCollection();
        services.AddAIRateLimiter(configuration.GetSection("RateLimiting"));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<AIRateLimitingOptions>>();
        Assert.Equal(3, monitor.CurrentValue.RequestsPerWindow);
        Assert.Equal(60, monitor.CurrentValue.WindowSeconds);
        configuration["RateLimiting:RequestsPerWindow"] = "5";
        configuration.Reload();
        Assert.Equal(5, monitor.CurrentValue.RequestsPerWindow);
    }

    [Fact]
    public void ConcreteBindingRetainsValidationFailures()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RequestsPerWindow"] = "0",
        }).Build();
        var services = new ServiceCollection();
        services.AddAIRateLimiter(configuration.GetSection("RateLimiting"));
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AIRateLimitingOptions>>().Value);
        Assert.Contains("AI:RateLimiting:RequestsPerWindow must be greater than 0 when provided.", error.Failures);
    }
}
