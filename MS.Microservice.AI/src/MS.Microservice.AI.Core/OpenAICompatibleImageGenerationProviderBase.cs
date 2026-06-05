using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleImageGenerationProviderBase : OpenAICompatibleMediaProviderBase, IAIImageGenerationProvider
{
    protected OpenAICompatibleImageGenerationProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public ValueTask<AIImageResponse> GenerateAsync(AIResolvedModel model, AIImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<AIImageResponse>(ExecuteAsync(
            AICapability.ImageGeneration,
            "image_generation",
            model,
            request.RequestId,
            () => CreateJsonRequest(RelativeImageGenerationPath, new
            {
                model = model.Model,
                prompt = request.Prompt,
                n = model.Count ?? 1,
                size = model.Size,
                quality = model.Quality,
                response_format = model.ResponseFormat ?? "b64_json",
            }),
            (httpResponse, requestCancellationToken) => ParseImageResponseAsync(httpResponse, AICapability.ImageGeneration, model, request.RequestId, requestCancellationToken),
            cancellationToken));
    }

    protected virtual string RelativeImageGenerationPath => "images/generations";

    protected Task<AIImageResponse> ParseImageResponseAsync(
        HttpResponseMessage httpResponse,
        AICapability capability,
        AIResolvedModel model,
        string? requestId,
        CancellationToken cancellationToken)
    {
        return ParseImageCoreResponseAsync(httpResponse, capability, model, requestId, cancellationToken);
    }

    protected async Task<AIImageResponse> ParseImageCoreResponseAsync(
        HttpResponseMessage httpResponse,
        AICapability capability,
        AIResolvedModel model,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(httpResponse, capability, model, requestId, cancellationToken).ConfigureAwait(false);
        }

        await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            throw new AIProviderException(
                $"AI provider '{Name}' returned an invalid image response.",
                AIErrorCodes.ResponseInvalid,
                capability,
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: GetProviderRequestId(httpResponse));
        }

        var images = new List<AIImageData>();
        foreach (var item in dataElement.EnumerateArray())
        {
            var url = TryGetString(item, "url");
            var revisedPrompt = TryGetString(item, "revised_prompt");
            AIBinaryContent? content = null;
            var base64 = TryGetString(item, "b64_json");

            if (!string.IsNullOrWhiteSpace(base64))
            {
                content = new AIBinaryContent
                {
                    Content = Convert.FromBase64String(base64),
                    ContentType = "image/png",
                    FileName = "image.png",
                };
            }

            if (url is null && content is null)
            {
                continue;
            }

            images.Add(new AIImageData
            {
                Url = url,
                Content = content,
                RevisedPrompt = revisedPrompt,
            });
        }

        if (images.Count == 0)
        {
            throw new AIProviderException(
                $"AI provider '{Name}' returned an empty image result.",
                AIErrorCodes.ResponseInvalid,
                capability,
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: GetProviderRequestId(httpResponse));
        }

        return new AIImageResponse
        {
            Provider = Name,
            Model = model.Model,
            Images = images,
            Usage = MapUsage(root),
            ProviderRequestId = GetProviderRequestId(httpResponse),
        };
    }
}