using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.Samples.EducationalImages;
using MS.Microservice.Samples.EducationalImages.Qwen;

namespace Microsoft.Extensions.DependencyInjection;

public static class EducationalImageServices
{
    public static IServiceCollection AddQwenEducationalImages(this IServiceCollection services)
    {
        services.TryAddSingleton<IReferenceImageEditClient, QwenReferenceImageEditAdapter>();
        return services;
    }
    /// <summary>
    /// Registers the word-image prompt generation pipeline, the end-to-end
    /// <see cref="ImageGenerationOrchestrator"/>, and the scene grouping agent.
    /// Requires <c>AddMicroserviceAI</c> to have been called first (provides
    /// <see cref="IAIChatClient"/> and <see cref="IAIImageGenerationClient"/>).
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="scenario">
    /// The chat scenario key for resolving the prompt-planning model from configuration.
    /// Uses <c>AI:Models:Chat:{scenario}</c> in <c>appsettings.json</c>.
    /// When <c>null</c>, defaults to <c>"ImagePromptPlanning"</c>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Configure the prompt-planning model and image generation model in <c>appsettings.json</c>:
    /// </para>
    /// <code>
    /// "AI": {
    ///   "Models": {
    ///     "Chat": {
    ///       "ImagePromptPlanning": {
    ///         "Provider": "OpenAI",
    ///         "Model": "gpt-4.1-mini"
    ///       }
    ///     },
    ///     "ImageGeneration": {
    ///       "Default": {
    ///         "Provider": "OpenAI",
    ///         "Model": "gpt-image-1",
    ///         "Size": "1024x1024"
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddImagePromptPipeline(this IServiceCollection services, string? scenario = null)
    {
        services.TryAddSingleton<IPlanGeneratorClient>(sp =>
            new PlanGeneratorClient(
                sp.GetRequiredService<IAIChatClient>(),
                sp.GetRequiredService<ILogger<PlanGeneratorClient>>(),
                scenario ?? PlanGeneratorClient.DefaultScenario));

        services.TryAddSingleton<ISceneGroupingAgent, SceneGroupingAgent>();
        services.TryAddTransient<WordImagePromptPipeline>();
        services.TryAddTransient<SentenceEditDeltaAgent>();
        services.TryAddTransient<ImageGenerationOrchestrator>();
        services.TryAddTransient<SentenceImageBatchOrchestrator>();
        return services;
    }

}