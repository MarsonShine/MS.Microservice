using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Azure;
using MS.Microservice.AI.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class AzureBuilderExtensions
{
    /// <summary>Registers Azure OpenAI using resources and deployments from the Azure provider configuration.</summary>
    public static AIBuilder AddAzure(this AIBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Section?.GetSection("Providers").GetSection(AzureOptions.ProviderName)
            ?? throw new InvalidOperationException("Call AddMicroserviceAI before registering the Azure AI provider.");

        // Binding and HTTP configuration should run once even if the host calls AddAzure twice.
        if (builder.Services.Any(service => service.ServiceType == typeof(AzureDeploymentRouter)))
        {
            return builder;
        }

        builder.Services.AddOptions<AzureOptions>().Bind(section);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AzureOptions>, AzureResourceSecrets>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, AzureOptionsValidator>());
        builder.Services.TryAddSingleton<AzureDeploymentRouter>();
        builder.Services.AddHttpClient(AzureOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(static _ => true);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIChatProvider, AzureChatProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIImageGenerationProvider, AzureImageProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIImageEditProvider, AzureImageProvider>());
        return builder;
    }
}
