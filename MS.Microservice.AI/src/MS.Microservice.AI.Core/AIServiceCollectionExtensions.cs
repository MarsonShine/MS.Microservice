using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.Core.Images;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering the AI module in the DI container.
/// </summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI module using the <c>AI</c> section of the application configuration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The application configuration. The <c>AI</c> key is used.</param>
    /// <returns>An <see cref="AIBuilder"/> for chaining provider registrations.</returns>
    public static AIBuilder AddMicroserviceAI(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(configuration);
        return services.AddMicroserviceAI(configuration.GetSection(AIOptions.SectionName));
    }

    /// <summary>
    /// Registers the AI module using an explicit configuration section.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="section">The configuration section bound to <see cref="AIOptions"/>.</param>
    /// <returns>An <see cref="AIBuilder"/> for chaining provider registrations.</returns>
    public static AIBuilder AddMicroserviceAI(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.TryAddSingleton<IConfiguration>(section);
        services.AddOptions<AIOptions>()
            .Bind(section)
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, AIOptionsValidator>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<AIProductionPipeline>();
        services.TryAddSingleton<IAIModelResolver, DefaultAIModelResolver>();
        services.TryAddSingleton<IAIProviderFactory, DefaultAIProviderFactory>();
        services.TryAddSingleton<IAIChatClient, RoutingAIChatClient>();
        services.TryAddSingleton<IAITtsClient, RoutingAITtsClient>();
        services.TryAddSingleton<IAIAsrClient, RoutingAIAsrClient>();
        services.TryAddSingleton<IAIImageGenerationClient, RoutingAIImageGenerationClient>();
        services.TryAddSingleton<IAIImageEditClient, RoutingAIImageEditClient>();

        services.AddAIRateLimiter(section.GetSection(AIRateLimitingOptions.SectionName));
        services.AddAICircuitBreaker(section.GetSection(AICircuitBreakerOptions.SectionName));
        services.AddAILogSanitizer(section.GetSection(AILogSanitizerOptions.SectionName));
        services.AddAISecretProvider(section.GetSection(AISecretProviderOptions.SectionName));
        services.AddAIPayloadLimits(section.GetSection(AIPayloadLimitOptions.SectionName));
        services.AddAICostAccounting(section.GetSection(AICostAccountingOptions.SectionName));

        return new AIBuilder(services);
    }

    /// <summary>Registers the AI rate limiter abstraction and built-in fixed-window implementation.</summary>
    public static IServiceCollection AddAIRateLimiter(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AIRateLimitingOptions, AIRateLimitingOptionsValidator>(services, section);
        services.TryAddSingleton<IAIRateLimiter, DefaultAIRateLimiter>();
        return services;
    }

    /// <summary>Registers the AI circuit breaker abstraction and built-in in-memory implementation.</summary>
    public static IServiceCollection AddAICircuitBreaker(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AICircuitBreakerOptions, AICircuitBreakerOptionsValidator>(services, section);
        services.TryAddSingleton<IAICircuitBreaker, DefaultAICircuitBreaker>();
        return services;
    }

    /// <summary>Registers the AI log sanitizer abstraction and default sensitive-field redactor.</summary>
    public static IServiceCollection AddAILogSanitizer(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AILogSanitizerOptions, AILogSanitizerOptionsValidator>(services, section);
        services.TryAddSingleton<IAILogSanitizer, DefaultAILogSanitizer>();
        return services;
    }

    /// <summary>Registers provider-neutral AI secret providers and post-configures provider API keys.</summary>
    public static IServiceCollection AddAISecretProvider(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AISecretProviderOptions, AISecretProviderOptionsValidator>(services, section);
        services.TryAddSingleton<EnvironmentAISecretProvider>();
        services.TryAddSingleton<ConfigurationAISecretProvider>();
        services.TryAddSingleton<IAISecretProvider, CompositeAISecretProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AIOptions>, AIOptionsSecretPostConfigure>());
        return services;
    }

    /// <summary>Registers AI payload limit options used by routing clients.</summary>
    public static IServiceCollection AddAIPayloadLimits(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AIPayloadLimitOptions, AIPayloadLimitOptionsValidator>(services, section);
        return services;
    }

    /// <summary>Registers AI cost accounting options and a no-op default reporter.</summary>
    public static IServiceCollection AddAICostAccounting(this IServiceCollection services, IConfigurationSection? section = null)
    {
        ConfigureValidatedOptions<AICostAccountingOptions, NoopValidator<AICostAccountingOptions>>(services, section);
        services.TryAddSingleton<IAICostReporter, NullAICostReporter>();
        return services;
    }

    /// <summary>
    /// Registers the word-image prompt generation pipeline.
    /// Requires <c>AddMicroserviceAI</c> to have been called first (provides <see cref="IAIChatClient"/>).
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="model">
    /// The model identifier used for visual plan generation (e.g. "gpt-4.1-mini").
    /// When <c>null</c>, defaults to "gpt-4.1-mini".
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddImagePromptPipeline(this IServiceCollection services, string? model = null)
    {
        var resolvedModel = model ?? "gpt-5.4-mini";

        services.TryAddSingleton<IPlanGeneratorClient>(sp =>
            new PlanGeneratorClient(
                sp.GetRequiredService<IAIChatClient>(),
                sp.GetRequiredService<ILogger<PlanGeneratorClient>>(),
                resolvedModel));

        services.TryAddTransient<WordImagePromptPipeline>();
        return services;
    }

    private static void ConfigureValidatedOptions<TOptions, TValidator>(IServiceCollection services, IConfigurationSection? section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        var builder = services.AddOptions<TOptions>();
        if (section is not null)
        {
            builder.Bind(section);
        }

        builder.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
    }

    private sealed class NoopValidator<TOptions> : IValidateOptions<TOptions>
        where TOptions : class
    {
        public ValidateOptionsResult Validate(string? name, TOptions options) => ValidateOptionsResult.Success;
    }
}
