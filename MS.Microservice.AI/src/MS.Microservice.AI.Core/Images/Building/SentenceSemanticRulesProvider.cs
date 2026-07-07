using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Provides sentence-specific deterministic semantic rules for injection into the final rich prompt.
/// </summary>
internal static class SentenceSemanticRulesProvider
{
    public static void AddRules(List<string> sections, WordImageInput input)
    {
        var text = input.TargetText ?? string.Empty;

        if (SentenceSemanticAnalyzer.IsProhibitive(text))
            sections.Add("This is a negative or prohibitive sentence. The image must show BOTH the prohibited action itself and a clear non-text warning or stopping cue. Do not show only a person raising a hand.");

        if (SentenceSemanticAnalyzer.IsCareful(text))
            sections.Add("Because the sentence is a safety warning, include a mild everyday safety reason in the scene. Keep it safe and child-friendly: no injury, no falling, no accident, no frightening danger.");

        if (SentenceSemanticAnalyzer.MentionsRunning(text))
            sections.Add("The action 'run' must be visually obvious: show a child in a running pose, with one foot lifted, body leaning forward, or clear movement between objects. Do not replace running with standing still.");

        if (SentenceSemanticAnalyzer.MentionsClassroom(text))
            sections.Add("The classroom must be immediately recognizable: include simple desks, chairs, windows, classroom floor layout, and a completely blank board. All boards and papers must remain blank with no visible writing.");
    }
}
