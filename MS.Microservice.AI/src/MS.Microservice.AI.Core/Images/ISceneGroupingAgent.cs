using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Abstraction for LLM-based scene grouping of educational sentence rows.
/// Groups a flat list of rows into visual context groups that share stable
/// characters, setting, and continuity policy for batch image generation.
/// </summary>
/// <remarks>
/// The default implementation <see cref="SceneGroupingAgent"/> uses
/// <see cref="IPlanGeneratorClient"/> to call an LLM for grouping decisions.
/// Host projects may provide a custom implementation for deterministic or
/// provider-specific grouping behavior.
/// </remarks>
public interface ISceneGroupingAgent
{
    /// <summary>
    /// Groups a list of word-image rows into visual context groups.
    /// Rows with pre-assigned <see cref="WordImageRow.SceneGroupId"/> values
    /// bypass the LLM and form deterministic groups. Remaining rows are sent
    /// to the LLM for semantic grouping.
    /// </summary>
    /// <param name="rows">The ordered list of input rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="SceneGroupingResult"/> containing the resolved groups
    /// and any rows whose grouping confidence is below the threshold.
    /// </returns>
    Task<SceneGroupingResult> GroupAsync(IReadOnlyList<WordImageRow> rows, CancellationToken ct = default);
}
