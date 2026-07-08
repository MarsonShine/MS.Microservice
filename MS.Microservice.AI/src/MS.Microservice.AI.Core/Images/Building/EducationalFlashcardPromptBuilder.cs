using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Builds the rich, constraint-heavy educational flashcard prompt.
/// This prompt is stored in the database for traceability but is NOT sent to Qwen directly
/// (use <see cref="QwenSafePromptBuilder"/> for the actual image generation call).
/// </summary>
public static class EducationalFlashcardPromptBuilder
{
    public static string Build(WordImageInput input, WordImagePromptPlan? plan)
    {
        var mainSubject = PromptNormalizer.NormalizeValue(plan?.MainSubject, input.MeaningHint, input.TargetText);
        var supportingVisual = PromptNormalizer.NormalizeValue(plan?.SupportingVisual);
        var actionOrGesture = PromptNormalizer.NormalizeValue(plan?.ActionOrGesture);
        var sceneSetting = PromptNormalizer.NormalizeSceneSetting(plan?.SceneSetting, input.ContentType);
        var backgroundHint = PromptNormalizer.NormalizeValue(plan?.BackgroundHint);
        var allowVisibleText = input.ContentType == WordImageCardType.Alphabet && plan?.AllowVisibleText == true;
        var overlayText = allowVisibleText ? PromptNormalizer.NormalizeOverlayText(plan?.OverlayText, input.TargetText) : string.Empty;

        var sections = new List<string>
        {
            "A simple 4:3 horizontal educational illustration.",
            "Bright cheerful children's storybook illustration style, semi-detailed friendly faces, clean smooth lines, flat soft colors with slightly higher brightness, gentle daylight, clean educational scene.",
            "Use a light, fresh, sunny color palette with clear but soft colors, minimal shadows, no dark or muddy tones.",
            "The image must contain zero visible text: no letters, no words, no numbers, no punctuation, no captions, no titles, no labels, no signs, no speech bubbles, and no readable markings.",
            "Do not render Chinese characters anywhere. Do not add display surfaces unless the sentence explicitly needs one.",
            "Use natural expressive eyes with visible irises, eye whites, and highlights. Do not use tiny dot eyes, bean-like eyes, or overly simplified facial features.",
            "All people must wear age-appropriate everyday clothing and appropriate footwear. Use sneakers or closed-toe shoes in school/public scenes, socks or slippers in home scenes. Strictly no bare feet.",
            "Use a balanced medium-wide composition. Keep all important people and objects fully inside the frame with clear safe margins. No cropped heads, cut-off hands, cut-off feet, missing limbs, or body parts touching the image edges."
        };

        BuildCardTypeSections(sections, input, plan, mainSubject, supportingVisual, actionOrGesture, sceneSetting, overlayText);

        if (!string.IsNullOrWhiteSpace(backgroundHint))
            sections.Add($"Background hint: {backgroundHint}. Keep it minimal and non-decorative.");

        if (allowVisibleText)
            sections.Add($"No visible text other than the exact target letter \"{overlayText}\".");
        else
            sections.Add("Do not render any visible words, captions, labels, punctuation, example sentences, or decorative typography inside the image.");

        if (input.ContentType != WordImageCardType.Alphabet)
            sections.Add("Objects that could contain text may appear only when essential to the sentence, and they must be visually blank.");

        sections.Add("Do not create a poster, wallpaper, portrait, fashion illustration, lifestyle scene, or decorative character artwork.");
        sections.Add("No decorative filler, extra toys, floor clutter, stars, sparkles, stickers, complex furniture, dramatic lighting, cinematic angles, ornate clothing, or beauty-shot styling.");
        sections.Add("No flags, flag symbols, national emblems, political symbols, military elements, maps with borders, violent, sexual, hateful, disturbing, or adult content.");
        sections.Add("All people must wear appropriate footwear (shoes, sneakers, or socks) matching the scene — strictly no bare feet.");
        sections.Add("Keep visible body parts complete and all action-related hands and feet clearly visible. Use a medium-wide composition with safe margins. Never crop at joints, leave important limbs outside the frame, or touch body parts to image edges.");
        sections.Add("Ensure the visual content accurately represents and is directly related to the intended meaning — avoid generic, random, or unrelated imagery.");

        if (plan?.NegativeElements?.Count > 0)
            sections.Add($"Also avoid: {string.Join(", ", plan.NegativeElements)}.");

        return string.Join(" ", sections.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static void BuildCardTypeSections(
        List<string> sections, WordImageInput input, WordImagePromptPlan? plan,
        string mainSubject, string supportingVisual, string actionOrGesture,
        string sceneSetting, string overlayText)
    {
        switch (input.ContentType)
        {
            case WordImageCardType.Alphabet:
                sections.Add("Plain white or off-white background with a thin soft pastel border.");
                sections.Add($"Show the exact target letter \"{overlayText}\" as a large central learning element in a clean bold sans-serif style.");
                sections.Add($"Below it, show one simple flat illustration of {mainSubject}.");
                if (!string.IsNullOrWhiteSpace(supportingVisual))
                    sections.Add($"Use {supportingVisual} only if it helps explain the letter-object connection.");
                sections.Add("Keep all elements large, centered, and easy to recognize.");
                break;

            case WordImageCardType.Word:
                sections.Add("Create a text-free teaching card illustration that explains the target meaning visually.");
                if (!string.IsNullOrWhiteSpace(input.MeaningHint))
                    sections.Add($"The image should clearly communicate the meaning \"{input.MeaningHint}\".");
                sections.Add($"Show one large central subject: {mainSubject}.");
                if (!string.IsNullOrWhiteSpace(supportingVisual))
                    sections.Add($"If needed, add only one small supporting visual: {supportingVisual}.");
                if (!string.IsNullOrWhiteSpace(sceneSetting))
                    sections.Add($"Set the scene in a simple, recognizable environment: {sceneSetting}. Keep the background clean and minimal — just enough to give context.");
                else
                    sections.Add("Use a clean, simple background — a plain light gradient or a minimal environmental hint appropriate to the word's meaning. Avoid completely empty white backgrounds unless the word has no spatial context.");
                sections.Add("Do not add excessive room clutter, decorative filler, or complex background details.");
                break;

            case WordImageCardType.Sentence:
                BuildSentenceSections(sections, input, plan, mainSubject, supportingVisual, actionOrGesture, sceneSetting);
                break;

            case WordImageCardType.Phrase:
                BuildPhraseSections(sections, input, plan, mainSubject, actionOrGesture, sceneSetting);
                break;

            default:
                sections.Add("Create a classroom-friendly text-free visual metaphor for the target concept.");
                sections.Add($"Main visual focus: {mainSubject}.");
                if (!string.IsNullOrWhiteSpace(sceneSetting))
                    sections.Add($"Minimal scene hint: {sceneSetting}.");
                sections.Add("Keep the background plain, with no room layout, wall decorations, or extra props, and make the concept immediately understandable.");
                break;
        }
    }

    private static void BuildSentenceSections(
        List<string> sections, WordImageInput input, WordImagePromptPlan? plan,
        string mainSubject, string supportingVisual, string actionOrGesture, string sceneSetting)
    {
        sections.Add("Create a text-free teaching illustration that communicates the sentence through one clear everyday event.");
        sections.Add("Keep one primary action and one primary environmental anchor. Avoid adding optional props, extra facilities, or secondary activities.");

        if (!string.IsNullOrWhiteSpace(input.MeaningHint))
            sections.Add($"The image should clearly communicate the meaning \"{input.MeaningHint}\".");

        sections.Add($"Main visual meaning: {mainSubject}.");

        if (!string.IsNullOrWhiteSpace(plan?.PrimaryActor))
            sections.Add($"Primary actor: {plan.PrimaryActor}.");
        if (!string.IsNullOrWhiteSpace(plan?.SecondaryActor))
            sections.Add($"Secondary actor: {plan.SecondaryActor}.");
        if (!string.IsNullOrWhiteSpace(plan?.RequiredAction))
            sections.Add($"Required visible action: {plan.RequiredAction}.");
        if (!string.IsNullOrWhiteSpace(plan?.ProhibitedAction))
            sections.Add($"Forbidden action that must still be visible: {plan.ProhibitedAction}.");
        if (!string.IsNullOrWhiteSpace(plan?.WarningCue))
            sections.Add($"Non-text warning cue: {plan.WarningCue}.");
        if (!string.IsNullOrWhiteSpace(plan?.SafetyCue))
            sections.Add($"Mild safety cue: {plan.SafetyCue}. Keep it safe and child-friendly, with no injury, no falling, and no accident.");
        if (!string.IsNullOrWhiteSpace(actionOrGesture))
            sections.Add($"Main action or gesture: {actionOrGesture}.");
        if (!string.IsNullOrWhiteSpace(supportingVisual))
            sections.Add($"Supporting visual: {supportingVisual}.");

        if (!string.IsNullOrWhiteSpace(sceneSetting))
        {
            sections.Add($"Scene setting: {sceneSetting}.");
            if (plan?.SettingCues?.Count > 0)
                sections.Add($"Use this single environmental anchor: {string.Join(", ", plan.SettingCues.Take(1))}.");
            else
                sections.Add("Use one simple environmental anchor that makes the location recognizable.");
        }
        else
        {
            sections.Add("Use a simple, recognizable everyday environment that directly matches the sentence context. Do not use a blank studio background for sentence cards.");
        }

        if (plan?.MustShow?.Count > 0)
            sections.Add($"The image must visibly include: {string.Join("; ", plan.MustShow)}.");

        SentenceSemanticRulesProvider.AddRules(sections, input);

        sections.Add("Use a balanced event composition, not a single static portrait. The scene should clearly show what is happening without becoming busy.");
        sections.Add("Use only necessary props and background objects that help explain the sentence meaning. Keep the scene clean, sparse, and uncluttered.");
    }

    private static void BuildPhraseSections(
        List<string> sections, WordImageInput input, WordImagePromptPlan? plan,
        string mainSubject, string actionOrGesture, string sceneSetting)
    {
        sections.Add("Create a simple text-free teaching scene that clearly shows the target expression through body language, action, and context.");

        if (!string.IsNullOrWhiteSpace(actionOrGesture))
            sections.Add($"Focus on this main action or gesture: {actionOrGesture}.");
        else
            sections.Add($"Main visual focus: {mainSubject}.");

        if (!string.IsNullOrWhiteSpace(sceneSetting))
        {
            sections.Add($"Scene setting: {sceneSetting}.");
            if (plan?.SettingCues?.Count > 0)
                sections.Add($"Include these simple text-free environmental cues: {string.Join(", ", plan.SettingCues)}.");
        }
        else
        {
            sections.Add("Use a simple recognizable everyday setting that supports the phrase. Avoid a completely empty white background unless the phrase is abstract.");
        }

        if (plan?.MustShow?.Count > 0)
            sections.Add($"The image must visibly include: {string.Join("; ", plan.MustShow)}.");

        sections.Add("Use a clean composition with all important people and objects fully inside the frame. No cropped figures or missing limbs.");
        sections.Add("Use only necessary props; avoid clutter and decorative filler.");
    }
}