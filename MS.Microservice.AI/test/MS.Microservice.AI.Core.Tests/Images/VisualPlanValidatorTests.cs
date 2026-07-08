using FluentAssertions;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.AI.Core.Images.Pipeline;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class VisualPlanValidatorTests
{
    [Fact]
    public void Validate_ShouldReturnEmpty_ForNonSentenceNonPhrase()
    {
        var input = new WordImageInput("A", "A", null, WordImageCardType.Alphabet);
        var plan = new WordImageVisualPlan();

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReturnEmpty_ForValidPlan()
    {
        var input = new WordImageInput("Hello!", "Hello!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child waving hello",
            MainSubject = "child waving"
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Prohibitive validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ShouldReportMissingProhibitedAction()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child standing still",
            ProhibitedAction = null,
            WarningCue = "teacher raises hand"
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportMissingWarningCue()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            ProhibitedAction = "running",
            WarningCue = null
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldPass_WhenProhibitedActionInMustShow()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            MustShow = ["a child running between desks"]
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().NotContain(item => item.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Careful validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ShouldReportMissingSafetyCue()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child walking",
            SafetyCue = null,
            MustShow = []
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("safety", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Classroom validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ShouldReportMissingClassroomSetting()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            SceneSetting = null,
            SettingCues = null
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("classroom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportMissingClassroomCues()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child running",
            SceneSetting = "a classroom",
            SettingCues = ["desks"] // Only 1 cue
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("environmental cues", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Running validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ShouldReportMissingRunningAction()
    {
        var input = new WordImageInput("Run fast!", "Run fast!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            VisualMeaning = "A child standing",
            RequiredAction = null,
            MustShow = []
        };

        var issues = VisualPlanValidator.Validate(input, plan);

        issues.Should().Contain(item => item.Contains("running", StringComparison.OrdinalIgnoreCase));
    }
}
