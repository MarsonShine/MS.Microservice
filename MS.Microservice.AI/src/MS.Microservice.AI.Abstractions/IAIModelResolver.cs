namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Resolves a concrete model configuration by combining request-level overrides,
/// scenario-specific settings, and provider defaults.
/// </summary>
public interface IAIModelResolver
{
    /// <summary>Resolves the model for a chat request.</summary>
    /// <exception cref="AIConfigurationException">No matching configuration found.</exception>
    AIResolvedModel ResolveChatModel(AIChatRequest request);

    /// <summary>Resolves the model for a text-to-speech request.</summary>
    /// <exception cref="AIConfigurationException">No matching configuration found.</exception>
    AIResolvedModel ResolveTtsModel(AITtsRequest request);

    /// <summary>Resolves the model for a speech recognition request.</summary>
    /// <exception cref="AIConfigurationException">No matching configuration found.</exception>
    AIResolvedModel ResolveAsrModel(AIAsrRequest request);

    /// <summary>Resolves the model for an image generation request.</summary>
    /// <exception cref="AIConfigurationException">No matching configuration found.</exception>
    AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request);

    /// <summary>Resolves the model for an image edit request.</summary>
    /// <exception cref="AIConfigurationException">No matching configuration found.</exception>
    AIResolvedModel ResolveImageEditModel(AIImageEditRequest request);
}