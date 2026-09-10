using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleAsrProviderBase : OpenAICompatibleMediaProviderBase, IAIAsrProvider
{
    protected OpenAICompatibleAsrProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public ValueTask<AIAsrResponse> RecognizeAsync(AIResolvedModel model, AIAsrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        var responseFormat = model.ResponseFormat?.Trim() ?? "verbose_json";

        return new ValueTask<AIAsrResponse>(ExecuteAsync(
            AICapability.Asr,
            "transcription",
            model,
            request.RequestId,
            () => CreateMultipartRequest(RelativeTranscriptionsPath, CreateMultipartContent(model, request, responseFormat)),
            async (httpResponse, requestCancellationToken) =>
            {
                if (!httpResponse.IsSuccessStatusCode)
                {
                    throw await CreateProviderExceptionAsync(httpResponse, AICapability.Asr, model, request.RequestId, requestCancellationToken).ConfigureAwait(false);
                }

                await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(requestCancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestCancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                var text = TryGetString(root, "text");

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new AIProviderException(
                        $"AI provider '{Name}' returned an invalid transcription response.",
                        AIErrorCodes.ResponseInvalid,
                        AICapability.Asr,
                        provider: Name,
                        model: model.Model,
                        scenario: model.Scenario,
                        requestId: request.RequestId,
                        providerRequestId: GetProviderRequestId(httpResponse));
                }

                var segments = new List<AIAsrSegment>();
                if (root.TryGetProperty("segments", out var segmentArray) && segmentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var segment in segmentArray.EnumerateArray())
                    {
                        var segmentText = TryGetString(segment, "text");
                        if (string.IsNullOrWhiteSpace(segmentText))
                        {
                            continue;
                        }

                        segments.Add(new AIAsrSegment
                        {
                            Text = segmentText,
                            StartSeconds = TryGetDouble(segment, "start"),
                            EndSeconds = TryGetDouble(segment, "end"),
                        });
                    }
                }

                return new AIAsrResponse
                {
                    Provider = Name,
                    Model = TryGetString(root, "model") ?? model.Model,
                    Text = text,
                    Language = TryGetString(root, "language") ?? model.Language,
                    Segments = segments,
                    Usage = MapUsage(root),
                    ProviderRequestId = GetProviderRequestId(httpResponse),
                };
            },
            cancellationToken));
    }

    protected virtual string RelativeTranscriptionsPath => "audio/transcriptions";

    private MultipartFormDataContent CreateMultipartContent(AIResolvedModel model, AIAsrRequest request, string responseFormat)
    {
        var content = new MultipartFormDataContent();
        var filePart = CreateBinaryContent(request.Audio);
        content.Add(filePart, "file", request.Audio.FileName ?? "audio.wav");
        content.Add(new StringContent(model.Model), "model");

        if (!string.IsNullOrWhiteSpace(model.Language))
        {
            content.Add(new StringContent(model.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(model.Prompt))
        {
            content.Add(new StringContent(model.Prompt), "prompt");
        }

        content.Add(new StringContent(responseFormat), "response_format");
        return content;
    }
}