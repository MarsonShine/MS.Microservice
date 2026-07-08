using FluentAssertions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageReferenceEditPolicyTests
{
    [Fact]
    public void ShouldUseReferenceEdit_ShouldReturnFalse_ForNullGroup()
    {
        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(null).Should().BeFalse();
    }

    [Fact]
    public void ShouldUseReferenceEdit_ShouldReturnFalse_ForSingleRowGroup()
    {
        var group = new VisualContextGroup { GroupId = "G1", RowIds = [1] };

        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group).Should().BeFalse();
    }

    [Theory]
    [InlineData("object_drill", true)]
    [InlineData("dialogue", true)]
    [InlineData("greeting", true)]
    [InlineData("self_introduction", true)]
    [InlineData("location_tour", true)]
    [InlineData("pre_assigned", true)]
    public void ShouldUseReferenceEdit_ShouldReturnTrue_ForEligibleGroupTypes(string groupType, bool expected)
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = groupType
        };

        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group).Should().Be(expected);
    }

    [Theory]
    [InlineData("safety_rules", false)]
    [InlineData("safety_rule", false)]
    [InlineData("instructional_sequence", false)]
    [InlineData("single_sentence", false)]
    [InlineData("uncertain", false)]
    [InlineData("exercise_sequence", false)]
    [InlineData("play_safety", false)]
    [InlineData("sports_safety", false)]
    [InlineData("action_sequence", false)]
    [InlineData("activity_sequence", false)]
    public void ShouldUseReferenceEdit_ShouldReturnFalse_ForIneligibleGroupTypes(string groupType, bool expected)
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = groupType
        };

        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group).Should().Be(expected);
    }

    [Fact]
    public void ShouldUseReferenceEdit_ShouldReturnTrue_ForHighConfidenceWithCharactersAndSetting()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = "dialogue",
            Confidence = 0.95,
            SceneSetting = "classroom doorway",
            Characters = [new CharacterProfile { Name = "Tom", Appearance = "schoolboy" }],
            ContinuityPolicy = "Same characters, same setting, same style"
        };

        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group).Should().BeTrue();
    }

    [Fact]
    public void ShouldUseReferenceEdit_ShouldReturnFalse_ForLowConfidence()
    {
        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = "unknown_type",
            Confidence = 0.5,
            SceneSetting = "somewhere",
            Characters = [new CharacterProfile { Name = "Tom" }]
        };

        SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group).Should().BeFalse();
    }
}
