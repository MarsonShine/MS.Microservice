using FluentAssertions;
using MS.Microservice.AI.Core.Images.Analysis;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceSemanticAnalyzerTests
{
    // ═══════════════════════════════════════════════════════════════
    // IsProhibitive
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Don't run in the classroom.", true)]
    [InlineData("don't touch that.", true)]
    [InlineData("Do not enter.", true)]
    [InlineData("Never give up.", true)]
    [InlineData("No smoking.", true)]
    [InlineData("no parking here.", true)]
    [InlineData("Run fast!", false)]
    [InlineData("Be careful.", false)]
    [InlineData("Hello, how are you?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsProhibitive_ShouldDetectProhibition(string? text, bool expected)
    {
        SentenceSemanticAnalyzer.IsProhibitive(text).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // IsCareful
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Be careful when you cross the street.", true)]
    [InlineData("be careful!", true)]
    [InlineData("Watch out for the car.", true)]
    [InlineData("watch out!", true)]
    [InlineData("Look out below!", true)]
    [InlineData("look out!", true)]
    [InlineData("Don't run.", false)]
    [InlineData("Hello.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCareful_ShouldDetectSafetyWarning(string? text, bool expected)
    {
        SentenceSemanticAnalyzer.IsCareful(text).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // MentionsClassroom
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Don't run in the classroom.", true)]
    [InlineData("Let's go to class.", true)]
    [InlineData("Our school is big.", true)]
    [InlineData("classroom rules.", true)]
    [InlineData("Play safely on the playground.", false)]
    [InlineData("Be careful at home.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MentionsClassroom_ShouldDetectClassroomContext(string? text, bool expected)
    {
        SentenceSemanticAnalyzer.MentionsClassroom(text).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // MentionsRunning
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Don't run!", true)]
    [InlineData("He runs fast.", true)]
    [InlineData("She is running in the park.", true)]
    [InlineData("Let's run together.", true)]
    [InlineData("Walk slowly.", false)]
    [InlineData("Be careful.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MentionsRunning_ShouldDetectRunningAction(string? text, bool expected)
    {
        SentenceSemanticAnalyzer.MentionsRunning(text).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // HasSignificantOverlap
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("a child running quickly", "running quickly", true)]
    [InlineData("running in the park", "a child running", true)]
    [InlineData("apple fruit banana", "orange grape melon", false)]
    [InlineData("", "something", false)]
    [InlineData("something", "", false)]
    [InlineData(null, "text", false)]
    [InlineData("text", null, false)]
    public void HasSignificantOverlap_ShouldDetectWordOverlap(string? a, string? b, bool expected)
    {
        SentenceSemanticAnalyzer.HasSignificantOverlap(a, b).Should().Be(expected);
    }

    [Fact]
    public void HasSignificantOverlap_ShouldBeSymmetric()
    {
        var result1 = SentenceSemanticAnalyzer.HasSignificantOverlap("a child running", "running child");
        var result2 = SentenceSemanticAnalyzer.HasSignificantOverlap("running child", "a child running");
        result1.Should().Be(result2);
    }
}
