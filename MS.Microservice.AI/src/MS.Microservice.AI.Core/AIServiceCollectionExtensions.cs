using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

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
        return services.AddMicroserviceAI(configuration.GetSection(AIOptions.SectionName));
    }

    /// <summary>
    /// Registers the AI module using an explicit configuration section.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="section">The configuration section bound to <see cref="AIOptions"/>.</param>
    /// <returns>An <see cref="AIBuilder"/> for chaining provider registrations.</returns>
    /// <remarks>
    /// This method registers:
    /// <list type="bullet">
    ///   <item><see cref="AIOptions"/> with <c>ValidateOnStart()</c></item>
    ///   <item><see cref="IAIModelResolver"/> → <see cref="DefaultAIModelResolver"/></item>
    ///   <item><see cref="IAIProviderFactory"/> → <see cref="DefaultAIProviderFactory"/></item>
    ///   <item>All five routing clients: <see cref="IAIChatClient"/>, <see cref="IAITtsClient"/>, <see cref="IAIAsrClient"/>, <see cref="IAIImageGenerationClient"/>, <see cref="IAIImageEditClient"/></item>
    ///   <item><see cref="TimeProvider.System"/> as a singleton</item>
    /// </list>
    /// Provider-specific registrations must be added by calling extension methods
    /// on the returned <see cref="AIBuilder"/> (e.g. <c>.AddOpenAI()</c>).
    /// </remarks>
    public static AIBuilder AddMicroserviceAI(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddOptions<AIOptions>()
            .Bind(section)
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, AIOptionsValidator>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IAIModelResolver, DefaultAIModelResolver>();
        services.TryAddSingleton<IAIProviderFactory, DefaultAIProviderFactory>();
        services.TryAddSingleton<IAIChatClient, RoutingAIChatClient>();
        services.TryAddSingleton<IAITtsClient, RoutingAITtsClient>();
        services.TryAddSingleton<IAIAsrClient, RoutingAIAsrClient>();
        services.TryAddSingleton<IAIImageGenerationClient, RoutingAIImageGenerationClient>();
        services.TryAddSingleton<IAIImageEditClient, RoutingAIImageEditClient>();

        return new AIBuilder(services);
    }
}