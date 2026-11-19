using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureImageProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureImageProvider> logger,
    AzureDeploymentRouter deployments)
    : OpenAICompatibleImageEditProviderBase(httpClientFactory, options, concurrency, timeProvider, logger)
{
    public override string Name => AzureOptions.ProviderName;
    protected override string HttpClientName => AzureOptions.HttpClientName;
    protected override string DefaultBaseAddress => throw new InvalidOperationException("Azure requires a configured deployment endpoint.");

    protected override (OpenAICompatibleRequestTarget Target, string? BodyModel, string ImagePartName) ResolveImageTarget(
        AIResolvedModel model, string relativePath)
    {
        var deployment = deployments.ResolveDeployment(model);
        if (deployment.Api is AzureDeploymentApi.Deployment)
        {
            return (deployment.CreateDeploymentRequestTarget(relativePath), null, "image");
        }

        var target = deployment.CreateRequestTarget($"{relativePath}?api-version=preview");
        return (target, target.Model, "image[]");
    }

    protected override string? GetImageResponseFormat(AIResolvedModel model)
    {
        if (model.ResponseFormat is not null && !string.Equals(model.ResponseFormat, "b64_json", StringComparison.OrdinalIgnoreCase))
        {
            throw new AIConfigurationException("Azure GPT Image supports only base64 output (b64_json).");
        }

        return null;
    }
}
