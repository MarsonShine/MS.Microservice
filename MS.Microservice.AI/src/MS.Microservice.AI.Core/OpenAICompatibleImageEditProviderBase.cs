using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleImageEditProviderBase : OpenAICompatibleImageGenerationProviderBase, IAIImageEditProvider
{
    protected OpenAICompatibleImageEditProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public ValueTask<AIImageResponse> EditAsync(AIResolvedModel model, AIImageEditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<AIImageResponse>(ExecuteAsync(
            AICapability.ImageEdit,
            "image_edit",
            model,
            request.RequestId,
            () => CreateMultipartRequest(RelativeImageEditPath, CreateMultipartContent(model, request)),
            (httpResponse, requestCancellationToken) => ParseImageResponseAsync(httpResponse, AICapability.ImageEdit, model, request.RequestId, requestCancellationToken),
            cancellationToken));
    }

    protected virtual string RelativeImageEditPath => "images/edits";

    private MultipartFormDataContent CreateMultipartContent(AIResolvedModel model, AIImageEditRequest request)
    {
        var content = new MultipartFormDataContent();
        content.Add(CreateBinaryContent(request.Image), "image", request.Image.FileName ?? "image.png");
        content.Add(new StringContent(model.Model), "model");
        content.Add(new StringContent(request.Prompt), "prompt");
        content.Add(new StringContent((model.Count ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), "n");
        content.Add(new StringContent(model.ResponseFormat ?? "b64_json"), "response_format");

        if (!string.IsNullOrWhiteSpace(model.Size))
        {
            content.Add(new StringContent(model.Size), "size");
        }

        if (!string.IsNullOrWhiteSpace(model.Quality))
        {
            content.Add(new StringContent(model.Quality), "quality");
        }

        if (request.Mask is not null)
        {
            content.Add(CreateBinaryContent(request.Mask), "mask", request.Mask.FileName ?? "mask.png");
        }

        return content;
    }
}