using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Abstraction for LLM-based visual plan generation.
/// The default implementation <see cref="PlanGeneratorClient"/> uses the chat client
/// from <c>MS.Microservice.AI.Abstractions</c>.
/// Host projects may provide a custom implementation for provider-specific behavior.
/// </summary>
public interface IPlanGeneratorClient
{
    /// <summary>
    /// Sends a system + user message pair and deserializes the response as JSON of type <typeparamref name="T"/>.
    /// </summary>
    Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string model, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Generates an alphabet-card visual plan.
    /// </summary>
    Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input, CancellationToken ct = default);

    /// <summary>
    /// Generates a visual plan for word / phrase / sentence cards.
    /// </summary>
    Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input, CancellationToken ct = default);
}
