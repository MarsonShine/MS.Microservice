using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleTtsProviderBase : OpenAICompatibleMediaProviderBase, IAITtsProvider
{
    protected OpenAICompatibleTtsProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public ValueTask<AITtsResponse> SynthesizeAsync(AIResolvedModel model, AITtsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        var voice = model.Voice?.Trim();
        if (string.IsNullOrWhiteSpace(voice))
        {
            throw new AIConfigurationException($"AI TTS voice is required for provider '{Name}'.");
        }

        var responseFormat = model.ResponseFormat?.Trim() ?? "mp3";

        return new ValueTask<AITtsResponse>(ExecuteAsync(
            AICapability.Tts,
            "speech",
            model,
            request.RequestId,
            () => CreateJsonRequest(RelativeSpeechPath, new
            {
                model = model.Model,
                input = request.Input,
                voice,
                response_format = responseFormat,
                speed = model.Speed,
            }, "application/octet-stream"),
            async (httpResponse, requestCancellationToken) =>
            {
                if (!httpResponse.IsSuccessStatusCode)
                {
                    throw await CreateProviderExceptionAsync(httpResponse, AICapability.Tts, model, request.RequestId, requestCancellationToken).ConfigureAwait(false);
                }

                var bytes = await httpResponse.Content.ReadAsByteArrayAsync(requestCancellationToken).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    throw new AIProviderException(
                        $"AI provider '{Name}' returned an empty speech payload.",
                        AIErrorCodes.ResponseInvalid,
                        AICapability.Tts,
                        provider: Name,
                        model: model.Model,
                        scenario: model.Scenario,
                        requestId: request.RequestId,
                        providerRequestId: GetProviderRequestId(httpResponse));
                }

                return new AITtsResponse
                {
                    Provider = Name,
                    Model = model.Model,
                    Voice = voice,
                    ResponseFormat = responseFormat,
                    Audio = new AIBinaryContent
                    {
                        Content = bytes,
                        ContentType = httpResponse.Content.Headers.ContentType?.MediaType ?? MapAudioContentType(responseFormat),
                        FileName = $"speech.{responseFormat}",
                    },
                };
            },
            cancellationToken));
    }

    protected virtual string RelativeSpeechPath => "audio/speech";

    private static string MapAudioContentType(string responseFormat)
    {
        return responseFormat.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "flac" => "audio/flac",
            "aac" => "audio/aac",
            "opus" => "audio/opus",
            "pcm" => "audio/L16",
            _ => "application/octet-stream",
        };
    }
}