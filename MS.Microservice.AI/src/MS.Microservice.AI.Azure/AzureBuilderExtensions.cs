using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Azure;
using MS.Microservice.AI.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class AzureBuilderExtensions
{
    /// <summary>Registers Azure using the host's AzureCloud section and Azure provider options.</summary>
    public static AIBuilder AddAzure(this AIBuilder builder)
        => AddAzureCore(builder, null);

    /// <summary>Registers Azure with an explicit AzureCloud configuration section.</summary>
    public static AIBuilder AddAzure(this AIBuilder builder, IConfigurationSection azureCloud)
    {
        ArgumentNullException.ThrowIfNull(azureCloud);
        return AddAzureCore(builder, azureCloud);
    }

    private static AIBuilder AddAzureCore(AIBuilder builder, IConfigurationSection? azureCloud)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Section?.GetSection("Providers").GetSection(AzureOptions.ProviderName)
            ?? throw new InvalidOperationException("Call AddMicroserviceAI before registering the Azure AI provider.");

        // Binding and HTTP configuration should run once even if the host calls AddAzure twice.
        if (builder.Services.Any(service => service.ServiceType == typeof(AzureDeploymentRouter)))
        {
            return builder;
        }

        builder.Services.AddOptions<AzureOptions>().Bind(section)
            .Configure<IConfiguration>((options, configuration) =>
                (azureCloud ?? configuration.GetSection("AzureCloud")).Bind(options.Cloud));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AzureOptions>, AzureResourceSecrets>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AIOptions>, AzureCloudRegistration>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, AzureOptionsValidator>());
        builder.Services.TryAddSingleton<AzureDeploymentRouter>();
        builder.Services.AddHttpClient(AzureOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(static _ => true);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIChatProvider, AzureChatProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIImageGenerationProvider, AzureImageProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIImageEditProvider, AzureImageProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAITtsProvider, AzureTtsProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIAsrProvider, AzureAsrProvider>());
        return builder;
    }
}
