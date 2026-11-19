using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleImageEditProviderBase : OpenAICompatibleImageGenerationProviderBase, IAIImageEditProvider
{
    protected OpenAICompatibleImageEditProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        AIProviderConcurrency concurrency,
        TimeProvider timeProvider,
        ILogger logger)
        : base(httpClientFactory, options, concurrency, timeProvider, logger)
    {
    }

    public virtual ValueTask<AIImageResponse> EditAsync(AIResolvedModel model, AIImageEditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<AIImageResponse>(ExecuteAsync(
            AICapability.ImageEdit,
            "image_edit",
            model,
            request.RequestId,
            () =>
            {
                var target = ResolveTarget(model, RelativeImageEditPath);
                return CreateMultipartRequest(target, CreateMultipartContent(model, request, target.Model));
            },
            (httpResponse, requestCancellationToken) => ParseImageResponseAsync(httpResponse, AICapability.ImageEdit, model, request.RequestId, requestCancellationToken),
            cancellationToken));
    }

    protected virtual string RelativeImageEditPath => "images/edits";

    protected virtual string ImagePartName => "image";

    private MultipartFormDataContent CreateMultipartContent(AIResolvedModel model, AIImageEditRequest request, string deploymentModel)
    {
        var responseFormat = GetImageResponseFormat(model);
        var content = new MultipartFormDataContent();
        content.Add(CreateBinaryContent(request.Image), ImagePartName, request.Image.FileName ?? "image.png");
        content.Add(new StringContent(deploymentModel), "model");
        content.Add(new StringContent(request.Prompt), "prompt");
        content.Add(new StringContent((model.Count ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), "n");
        if (responseFormat is not null)
        {
            content.Add(new StringContent(responseFormat), "response_format");
        }

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
