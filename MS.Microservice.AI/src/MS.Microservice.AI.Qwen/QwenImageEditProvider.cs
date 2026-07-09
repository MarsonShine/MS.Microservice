using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

internal sealed class QwenImageEditProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    TimeProvider timeProvider,
    ILogger<QwenImageEditProvider> logger) : OpenAICompatibleImageEditProviderBase(httpClientFactory, options, timeProvider, logger)
{
    public override string Name => QwenProviderDefaults.ProviderName;

    protected override string HttpClientName => QwenProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => QwenProviderDefaults.DefaultBaseAddress;

    private const string MultimodalGenerationEndpointKey = "MultimodalGeneration";

    private const string DefaultMultimodalGenerationPath = "api/v1/services/aigc/multimodal-generation/generation";

    public override async ValueTask<AIImageResponse> EditAsync(
        AIResolvedModel model,
        AIImageEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        // If no reference URL, fall back to the OpenAI-compatible multipart /images/edits behavior.
        if (string.IsNullOrWhiteSpace(request.ReferenceImageUrl))
        {
            return await base.EditAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        // ── Qwen multimodal reference-image edit path ──

        var endpoint = ResolveMultimodalEndpoint();
        var size = NormalizeSize(model.Size ?? "1024x1024");
        var negativePrompt = string.IsNullOrWhiteSpace(request.NegativePrompt) ? " " : request.NegativePrompt;
        var prompt = WrapEditPromptWithSourceProtection(request.Prompt);

        var payload = new
        {
            model = model.Model,
            input = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { image = request.ReferenceImageUrl },
                            new { text = prompt }
                        }
                    }
                }
            },
            parameters = new
            {
                n = model.Count ?? 1,
                negative_prompt = negativePrompt,
                prompt_extend = false,
                watermark = false,
                size
            }
        };

        return await ExecuteAsync(
            AICapability.ImageEdit,
            "image_edit",
            model,
            request.RequestId,
            () => CreateJsonRequest(endpoint, payload),
            (httpResponse, requestCancellationToken) =>
                ParseMultimodalResponseAsync(httpResponse, AICapability.ImageEdit, model, request.RequestId, requestCancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private Uri ResolveMultimodalEndpoint()
    {
        if (ProviderOptions.Endpoints.TryGetValue(MultimodalGenerationEndpointKey, out var configuredEndpoint)
            && !string.IsNullOrWhiteSpace(configuredEndpoint)
            && Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        // Fall back to default path under the configured base address.
        return new Uri(new Uri(GetBaseAddress()), DefaultMultimodalGenerationPath);
    }

    private string GetBaseAddress()
    {
        var baseAddress = string.IsNullOrWhiteSpace(ProviderOptions.BaseAddress)
            ? DefaultBaseAddress
            : ProviderOptions.BaseAddress;

        return baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : $"{baseAddress}/";
    }

    /// <summary>
    /// Normalizes <c>1024x1024</c> to Qwen's required <c>1024*1024</c> format.
    /// </summary>
    private static string NormalizeSize(string size)
    {
        return size.Replace('x', '*').Replace('X', '*');
    }

    private async Task<AIImageResponse> ParseMultimodalResponseAsync(
        HttpResponseMessage httpResponse,
        AICapability capability,
        AIResolvedModel model,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(httpResponse, capability, model, requestId, cancellationToken)
                .ConfigureAwait(false);
        }

        await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;

        // Parse Qwen multimodal response: output.choices[].message.content[].image
        var images = new List<AIImageData>();

        if (root.TryGetProperty("output", out var output)
            && output.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("message", out var message))
                    continue;

                if (!message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in content.EnumerateArray())
                {
                    var imageUrl = TryGetString(item, "image");
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        images.Add(new AIImageData { Url = imageUrl });
                    }
                }
            }
        }

        if (images.Count == 0)
        {
            throw new AIProviderException(
                $"AI provider '{Name}' returned an empty multimodal image result.",
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
