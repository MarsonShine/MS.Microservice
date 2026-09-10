namespace MS.Microservice.AI.Qwen;

public static class QwenProviderDefaults
{
    public const string ProviderName = "Qwen";
    public const string HttpClientName = "MS.Microservice.AI.Qwen";
    public const string DefaultBaseAddress = "https://dashscope.aliyuncs.com/compatible-mode/v1/";

    /// <summary>Key for <see cref="AIProviderRegistrationOptions.Endpoints"/> to override the multimodal generation endpoint.</summary>
    public const string MultimodalGenerationEndpointKey = "MultimodalGeneration";

    /// <summary>Default Qwen multimodal generation API endpoint.</summary>
    public const string DefaultMultimodalGenerationEndpoint = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
}