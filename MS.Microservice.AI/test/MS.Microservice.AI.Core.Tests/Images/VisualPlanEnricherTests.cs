using FluentAssertions;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.AI.Core.Images.Pipeline;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class VisualPlanEnricherTests
{
    // ═══════════════════════════════════════════════════════════════
    // Enrich - basic initialization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldInitializeNullCollections()
    {
        var input = new WordImageInput("Hello", "Hello", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.MustShow.Should().NotBeNull();
        plan.MustNotShow.Should().NotBeNull();
        plan.SettingCues.Should().NotBeNull();
    }

    [Fact]
    public void Enrich_ShouldSkipNonSentenceNonPhrase()
    {
        var input = new WordImageInput("A", "A", null, WordImageCardType.Alphabet);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "letter A",
            SettingCues = ["desks", "chairs"]
        };

        VisualPlanEnricher.Enrich(input, plan);

        // Alphabet cards should not have sentence-level enrichment
        plan.SentenceIntent.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // Enrich - prohibitive sentences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldAddProhibitionMustShow_ForProhibitiveSentence()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.MustShow.Should().Contain(item => item.Contains("forbidden action", StringComparison.OrdinalIgnoreCase));
        plan.MustShow.Should().Contain(item => item.Contains("warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enrich_ShouldSetSentenceIntentToProhibition()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.SentenceIntent.Should().Be("prohibition");
    }

    [Fact]
    public void Enrich_ShouldNotOverrideExistingSentenceIntent()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan { SentenceIntent = "warning" };

        VisualPlanEnricher.Enrich(input, plan);

        plan.SentenceIntent.Should().Be("warning");
    }

    // ═══════════════════════════════════════════════════════════════
    // Enrich - careful / safety sentences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldAddSafetyMustShow_ForCarefulSentence()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.MustShow.Should().Contain(item => item.Contains("safety reason", StringComparison.OrdinalIgnoreCase));
        plan.MustNotShow.Should().Contain("injury");
        plan.MustNotShow.Should().Contain("falling accident");
        plan.MustNotShow.Should().Contain("frightening danger");
    }

    [Fact]
    public void Enrich_ShouldSetDefaultSafetyCue_ForCarefulSentence()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.SafetyCue.Should().NotBeNullOrWhiteSpace();
        plan.SafetyCue.Should().Contain("obstacle");
    }

    // ═══════════════════════════════════════════════════════════════
    // Enrich - running sentences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldAddRunningMustShow_ForRunningSentence()
    {
        var input = new WordImageInput("Run fast!", "Run fast!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.MustShow.Should().Contain(item => item.Contains("running", StringComparison.OrdinalIgnoreCase));
        plan.RequiredAction.Should().Contain("running");
    }

    [Fact]
    public void Enrich_ShouldSetProhibitedAction_ForProhibitiveRunningSentence()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.ProhibitedAction.Should().Be("running");
    }

    // ═══════════════════════════════════════════════════════════════
    // Enrich - classroom sentences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldAddClassroomCues_WhenMentionsClassroom()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();

        VisualPlanEnricher.Enrich(input, plan);

        plan.SceneSetting.Should().Contain("classroom");
        plan.MustShow.Should().Contain(item => item.Contains("classroom environment", StringComparison.OrdinalIgnoreCase));
        // Enricher now only adds "desks" as a setting cue (simplified from 4 to 1)
        plan.SettingCues.Should().HaveCount(1);
        plan.SettingCues[0].Should().Be("desks");
    }

    // ═══════════════════════════════════════════════════════════════
    // Enrich - normalization caps
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enrich_ShouldNormalizeMustShow_ToMax8()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            MustShow = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]
        };

        VisualPlanEnricher.Enrich(input, plan);

        plan.MustShow.Should().HaveCountLessThanOrEqualTo(8);
    }

    [Fact]
    public void Enrich_ShouldNormalizeSettingCues_ToMax1()
    {
        var input = new WordImageInput("Run and play in the big beautiful park.", "Run and play in the big beautiful park.", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            SettingCues = ["a slide", "a swing", "grass", "trees"]
        };

        VisualPlanEnricher.Enrich(input, plan);

        plan.SettingCues.Should().HaveCountLessThanOrEqualTo(1);
    }
}
