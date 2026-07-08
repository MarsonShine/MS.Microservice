using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SceneGroupingAgentTests
{
    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - empty input
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldReturnEmpty_ForEmptyRows()
    {
        var agent = CreateAgent();

        var result = await agent.GroupAsync([]);

        result.Groups.Should().BeEmpty();
        result.UncertainRowIds.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - pre-assigned groups
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldRespectPreAssignedGroups()
    {
        var agent = CreateAgent();
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Hello!", Chinese = "你好！", OrderIndex = 1, SceneGroupId = "G1", Speaker = "Tom", SceneHint = "classroom doorway" },
            new() { RowId = 2, English = "Hi!", Chinese = "嗨！", OrderIndex = 2, SceneGroupId = "G1", Speaker = "Amy" },
        };

        var result = await agent.GroupAsync(rows);

        result.Groups.Should().HaveCount(1);
        var group = result.Groups[0];
        group.GroupId.Should().Be("G1");
        group.RowIds.Should().BeEquivalentTo([1L, 2L]);
        group.GroupType.Should().Be("pre_assigned");
        group.Confidence.Should().Be(1.0);
        group.Members.Should().HaveCount(2);
        group.Members.First(m => m.RowId == 1).Speaker.Should().Be("Tom");
    }

    [Fact]
    public async Task GroupAsync_ShouldHandleMixedPreAssignedAndUnassigned()
    {
        var agent = CreateAgent();
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Hello!", Chinese = "你好！", OrderIndex = 1, SceneGroupId = "G1", Speaker = "Tom" },
            new() { RowId = 2, English = "An apple.", Chinese = "一个苹果。", OrderIndex = 2 }, // unassigned
        };

        var result = await agent.GroupAsync(rows);

        // Should have 2 groups: one pre-assigned (G1) and one LLM/fallback for row 2
        result.Groups.Should().HaveCount(2);
        result.Groups.Should().Contain(g => g.GroupId == "G1");
        result.Groups.Should().Contain(g => g.RowIds.Contains(2L));
    }

    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - fallback when LLM returns no groups
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldFallbackToStandalone_WhenLlmReturnsNull()
    {
        var planClient = new NullGroupingPlanClient();
        var agent = new SceneGroupingAgent(planClient, NullLogger<SceneGroupingAgent>.Instance);
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Hello!", Chinese = "你好！", OrderIndex = 1 },
            new() { RowId = 2, English = "An apple.", Chinese = "一个苹果。", OrderIndex = 2 },
        };

        var result = await agent.GroupAsync(rows);

        result.Groups.Should().HaveCount(2);
        result.Groups.Should().AllSatisfy(g => g.GroupType.Should().Be("single_sentence"));
        result.Groups.SelectMany(g => g.RowIds).Should().BeEquivalentTo([1L, 2L]);
    }

    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - all rows covered
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldCoverAllRows()
    {
        var agent = CreateAgent();
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Hello!", Chinese = "你好！", OrderIndex = 1, SceneGroupId = "G1" },
            new() { RowId = 2, English = "Hi!", Chinese = "嗨！", OrderIndex = 2 },
            new() { RowId = 3, English = "Good morning.", Chinese = "早上好。", OrderIndex = 3 },
        };

        var result = await agent.GroupAsync(rows);

        // Every row should be in exactly one group
        var allRowIds = result.Groups.SelectMany(g => g.RowIds).ToList();
        allRowIds.Should().BeEquivalentTo([1L, 2L, 3L]);
        allRowIds.Should().OnlyHaveUniqueItems();
    }

    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - members should have visual focus
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldPopulateMemberVisualFocus()
    {
        var agent = CreateAgent();
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Hello!", Chinese = "你好！", OrderIndex = 1, SceneGroupId = "G1", Speaker = "Tom" },
        };

        var result = await agent.GroupAsync(rows);

        var member = result.Groups[0].Members[0];
        member.VisualFocus.Should().Be("Hello!");
        member.Speaker.Should().Be("Tom");
    }

    // ═══════════════════════════════════════════════════════════════
    // GroupAsync - groups ordered by OrderIndex
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GroupAsync_ShouldOrderGroupsByOrderIndex()
    {
        var agent = CreateAgent();
        var rows = new List<WordImageRow>
        {
            new() { RowId = 3, English = "Third.", Chinese = "第三。", OrderIndex = 3 },
            new() { RowId = 1, English = "First.", Chinese = "第一。", OrderIndex = 1 },
            new() { RowId = 2, English = "Second.", Chinese = "第二。", OrderIndex = 2 },
        };

        var result = await agent.GroupAsync(rows);

        // Groups should be ordered by the min RowId's OrderIndex
        result.Groups[0].RowIds.Min().Should().Be(1);
        result.Groups[1].RowIds.Min().Should().Be(2);
        result.Groups[2].RowIds.Min().Should().Be(3);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test doubles
    // ═══════════════════════════════════════════════════════════════

    private static SceneGroupingAgent CreateAgent()
    {
        return new SceneGroupingAgent(new NullGroupingPlanClient(), NullLogger<SceneGroupingAgent>.Instance);
    }

    private sealed class NullGroupingPlanClient : IPlanGeneratorClient
    {
        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(null);

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input, CancellationToken ct = default)
            => Task.FromResult<WordImagePromptPlan?>(null);

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input, CancellationToken ct = default)
            => Task.FromResult<WordImageVisualPlan?>(null);
    }
}
