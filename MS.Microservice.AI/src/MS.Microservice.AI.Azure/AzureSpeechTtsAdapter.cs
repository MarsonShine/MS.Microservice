using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.Threading;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureSpeechTtsAdapter(IHttpClientFactory clients, AIProviderConcurrency concurrency, TimeProvider clock)
{
    private const string Provider = AzureOptions.ProviderName;
    private const string KeyHeader = "Ocp-Apim-Subscription-Key";
    private const string FormatHeader = "X-Microsoft-OutputFormat";
    private static readonly XNamespace Ssml = "http://www.w3.org/2001/10/synthesis";

    public async ValueTask<AITtsResponse> SynthesizeAsync(AzureSpeechTtsOptions speech, AIProviderRegistrationOptions registration,
        AIResolvedModel model, AITtsRequest request, CancellationToken cancellationToken)
    {
        var endpoint = speech.GetSynthesisEndpoint() ?? throw new AIConfigurationException("Azure AI Speech synthesis endpoint is invalid.");
        var voice = model.Voice?.Trim() ?? speech.Voice?.Trim();
        if (string.IsNullOrWhiteSpace(voice))
        {
            throw new AIConfigurationException("Azure AI Speech requires a voice, for example zh-CN-XiaoxiaoNeural.");
        }

        var format = model.ResponseFormat?.Trim().ToLowerInvariant() ?? "mp3";
        var (outputFormat, contentType) = format switch
        {
            "mp3" => ("audio-24khz-96kbitrate-mono-mp3", "audio/mpeg"),
            "wav" => ("riff-24khz-16bit-mono-pcm", "audio/wav"),
            "opus" => ("ogg-24khz-16bit-mono-opus", "audio/ogg"),
            "pcm" => ("raw-24khz-16bit-mono-pcm", "audio/L16"),
            _ => throw new AIConfigurationException("Azure AI Speech response format must be mp3, wav, opus or pcm."),
        };
        var ssml = BuildSsml(voice, request.Input, model.Speed);

        using var lease = await concurrency.GetRequired(Provider).AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var client = clients.CreateClient(AzureOptions.HttpClientName);
        return await AIHttpExecution.ExecuteAsync(Provider, AICapability.Tts, model, request.RequestId, clock, async token =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
            };
            foreach (var (name, value) in registration.Headers)
            {
                if (!name.Equals(KeyHeader, StringComparison.OrdinalIgnoreCase) && !name.Equals(FormatHeader, StringComparison.OrdinalIgnoreCase))
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            message.Headers.Add(KeyHeader, speech.Secret);
            message.Headers.Add(FormatHeader, outputFormat);
            if (!message.Headers.Contains("User-Agent"))
            {
                message.Headers.UserAgent.ParseAdd(AzureOptions.HttpClientName);
            }

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            await AIResponseReader.BufferAsync(response, registration, Provider, AICapability.Tts, model, request.RequestId, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await AIProviderErrors.CreateAsync(Provider, response, AICapability.Tts, model, request.RequestId, token).ConfigureAwait(false);
            }

            var audio = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (audio.Length == 0)
            {
                throw new AIProviderException("Azure AI Speech returned an empty audio payload.", AIErrorCodes.ResponseInvalid, AICapability.Tts,
                    provider: Provider, model: model.Model, scenario: model.Scenario, requestId: request.RequestId,
                    providerRequestId: AIProviderErrors.GetProviderRequestId(response));
            }

            return new AITtsResponse
            {
                Provider = Provider,
                Model = model.Model,
                Voice = voice,
                ResponseFormat = format,
                Audio = new AIBinaryContent
                {
                    Content = audio,
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? contentType,
                    FileName = $"speech.{format}",
                },
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSsml(string voice, string input, double? speed)
    {
        if (speed is { } value && (!double.IsFinite(value) || value is < 0.5 or > 2))
        {
            throw new AIConfigurationException("Azure AI Speech speed must be between 0.5 and 2.");
        }

        try
        {
            // The common TTS request contains plain text. Writing it as an XML node preserves that contract.
            XText text = new(XmlConvert.VerifyXmlChars(input));
            XmlConvert.VerifyXmlChars(voice);
            var localeEnd = voice.LastIndexOf('-');
            var locale = localeEnd > voice.IndexOf('-') ? voice[..localeEnd] : "en-US";
            var content = speed is { } rate
                ? (XNode)new XElement(Ssml + "prosody", new XAttribute("rate", rate.ToString(CultureInfo.InvariantCulture)), text)
                : text;
            return new XElement(Ssml + "speak", new XAttribute("version", "1.0"), new XAttribute(XNamespace.Xml + "lang", locale),
                new XElement(Ssml + "voice", new XAttribute("name", voice), content)).ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException exception)
        {
            throw new AIConfigurationException("Azure AI Speech input or voice contains characters that SSML cannot encode.", exception);
        }
    }
}
