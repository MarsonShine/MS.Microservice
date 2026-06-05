using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class AIServiceCollectionExtensions
{
    public static AIBuilder AddMicroserviceAI(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddMicroserviceAI(configuration.GetSection(AIOptions.SectionName));
    }

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