namespace MS.Microservice.AI.Azure;

internal sealed class AzureSpeechTtsOptions
{
    public const string ModelAlias = "azure-speech";
    private const string SynthesisPath = "/cognitiveservices/v1";
    private const string TokenHostSuffix = ".api.cognitive.microsoft.com";
    private const string SpeechHostSuffix = ".tts.speech.microsoft.com";

    public string? Secret { get; set; }
    public string? Region { get; set; }
    public string? Endpoint { get; set; }
    public string? Voice { get; set; }

    public static bool IsSpeechModel(string model) => string.Equals(model, ModelAlias, StringComparison.OrdinalIgnoreCase);

    public Uri? GetSynthesisEndpoint()
    {
        var region = Region?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(region) || !char.IsAsciiLetter(region[0]) || !region.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        var speechHost = $"{region}{SpeechHostSuffix}";
        var regionalEndpoint = new Uri($"https://{speechHost}{SynthesisPath}");
        if (Endpoint is null)
        {
            return regionalEndpoint;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            return null;
        }

        if (endpoint.Host.EndsWith(TokenHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return endpoint.IsDefaultPort && endpoint.Host.Equals($"{region}{TokenHostSuffix}", StringComparison.OrdinalIgnoreCase)
                && endpoint.AbsolutePath is "/" or "/sts/v1.0/issueToken"
                ? regionalEndpoint
                : null;
        }

        if (endpoint.Host.EndsWith(SpeechHostSuffix, StringComparison.OrdinalIgnoreCase)
            && !endpoint.Host.Equals(speechHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return endpoint.AbsolutePath is "/" or SynthesisPath or "/cognitiveservices/v1/"
            ? new Uri(endpoint, SynthesisPath)
            : null;
    }
}
