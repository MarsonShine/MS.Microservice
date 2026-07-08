using FluentAssertions;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.AI.Core.Images.Pipeline;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class VisualPlanRepairerTests
{
    [Fact]
    public void Repair_ShouldInitializeNullCollections()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string> { "Missing visible forbidden action." };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.MustShow.Should().NotBeNull();
        plan.MustNotShow.Should().NotBeNull();
        plan.SettingCues.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // Running / forbidden repair
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Repair_ShouldSetRequiredAndProhibitedAction_ForRunningIssue()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string> { "Missing visible forbidden/prohibited action." };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.RequiredAction.Should().Contain("running");
        plan.ProhibitedAction.Should().Be("running");
        plan.MustShow.Should().Contain(item => item.Contains("running pose", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Warning repair
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Repair_ShouldSetWarningCue_ForWarningIssue()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string> { "Missing non-text warning or stopping cue." };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.WarningCue.Should().NotBeNullOrWhiteSpace();
        plan.WarningCue.Should().Contain("palm");
        plan.MustShow.Should().Contain(item => item.Contains("warning", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Safety repair
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Repair_ShouldSetSafetyCue_ForSafetyIssue()
    {
        var input = new WordImageInput("Be careful!", "Be careful!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string> { "Missing mild safety reason for 'Be careful'." };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.SafetyCue.Should().NotBeNullOrWhiteSpace();
        plan.SafetyCue.Should().Contain("desks");
        plan.MustShow.Should().Contain(item => item.Contains("safety reason", StringComparison.OrdinalIgnoreCase));
        plan.MustNotShow.Should().Contain("injury");
        plan.MustNotShow.Should().Contain("falling");
    }

    // ═══════════════════════════════════════════════════════════════
    // Classroom repair
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Repair_ShouldSetClassroomSetting_ForClassroomIssue()
    {
        var input = new WordImageInput("Don't run in the classroom.", "Don't run in the classroom.", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string> { "Missing recognizable classroom setting." };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.SceneSetting.Should().Be("a bright classroom");
        // NormalizeList caps SettingCues at 1, so only "desks" survives
        plan.SettingCues.Should().HaveCount(1);
        plan.SettingCues[0].Should().Be("desks");
        plan.MustShow.Should().Contain(item => item.Contains("classroom environment", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Multiple issues
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Repair_ShouldHandleMultipleIssues()
    {
        var input = new WordImageInput("Don't run in the classroom!", "Don't run in the classroom!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan();
        var issues = new List<string>
        {
            "Missing visible forbidden/prohibited action.",
            "Missing non-text warning or stopping cue.",
            "Missing recognizable classroom setting."
        };

        VisualPlanRepairer.Repair(input, plan, issues);

        plan.RequiredAction.Should().Contain("running");
        plan.ProhibitedAction.Should().Be("running");
        plan.WarningCue.Should().NotBeNullOrWhiteSpace();
        plan.SceneSetting.Should().Be("a bright classroom");
        plan.SettingCues.Should().Contain("desks");
    }

    [Fact]
    public void Repair_ShouldNotDuplicateExistingValues()
    {
        var input = new WordImageInput("Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = new WordImageVisualPlan
        {
            RequiredAction = "a child running fast",
            ProhibitedAction = "running fast",
            MustShow = ["a child clearly running or about to run, with a visible running pose"]
        };
        var issues = new List<string> { "Missing visible forbidden/prohibited action." };

        VisualPlanRepairer.Repair(input, plan, issues);

        // Should preserve original values
        plan.RequiredAction.Should().Be("a child running fast");
        plan.ProhibitedAction.Should().Be("running fast");
    }
}
