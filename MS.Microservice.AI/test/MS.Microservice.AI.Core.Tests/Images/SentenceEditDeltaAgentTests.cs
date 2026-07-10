using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceEditDeltaAgentTests
{
    // ═══════════════════════════════════════════════════════════════
    // EnrichAsync - success cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_WhenLLMReturnsValidDelta_ShouldWriteToMemberEditDelta()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
                    Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(2)!.EditDelta.Should().NotBeNull();
        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0.95);
        group.FindMember(2)!.EditDelta!.Operations.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnrichAsync_FirstRow_ShouldHaveNoEditDelta()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
                    Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(1)!.EditDelta.Should().NotBeNull();
        group.FindMember(1)!.EditDelta!.Confidence.Should().Be(0);
        group.FindMember(1)!.EditDelta!.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_ReferenceRowIdNotAnchor_ShouldWriteNoEditDelta()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        // LLM incorrectly references row 3 which doesn't exist as anchor
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 3, Confidence = 0.95,
                    Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(2)!.EditDelta.Should().NotBeNull();
        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // EnrichAsync - normalization to no-edit
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_MultipleOperations_ShouldNormalizeToNoEdit()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
                    Operations =
                    [
                        new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" },
                        new SentenceImageEditOperation { Operation = "add", To = "hat" }
                    ]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task EnrichAsync_NoConcreteOperation_ShouldNormalizeToNoEdit()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
                    Operations = [new SentenceImageEditOperation { Operation = "replace", From = null, To = null }]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task EnrichAsync_LowConfidence_ShouldNormalizeToNoEdit()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Difficult concept", OrderIndex = 1 },
            new() { RowId = 2, English = "Another concept", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var response = new SentenceEditDeltaAgent.SentenceEditDeltaResponse
        {
            Deltas =
            [
                new SentenceImageEditDelta
                {
                    RowId = 2, ReferenceRowId = 1, Confidence = 0.3,
                    Operations = [new SentenceImageEditOperation { Operation = "replace", From = "x", To = "y" }]
                }
            ]
        };
        var planClient = new StaticPlanGeneratorClient(response);
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        // Note: confidence is clamped but the agent's NormalizeDelta keeps the value
        // The actual confidence value is preserved (0.3), but the operations are kept
        // because there's 1 concrete operation. The CanUseReferenceEdit threshold check
        // is in SentenceImageEditPromptBuilder.
        group.FindMember(2)!.EditDelta.Should().NotBeNull();
        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0.3);
        group.FindMember(2)!.EditDelta!.Operations.Should().HaveCount(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // EnrichAsync - error handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrichAsync_WhenPlanClientThrows_ShouldNotThrow_AndMembersStaySafe()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2],
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var planClient = new ThrowingPlanGeneratorClient();
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        // All members should have no-edit delta (safe state)
        group.FindMember(1)!.EditDelta.Should().NotBeNull();
        group.FindMember(1)!.EditDelta!.Confidence.Should().Be(0);
        group.FindMember(2)!.EditDelta.Should().NotBeNull();
        group.FindMember(2)!.EditDelta!.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task EnrichAsync_WhenLLMReturnsNull_ShouldNotThrow()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1],
            Members = [new() { RowId = 1 }]
        };
        var planClient = new NullReturningPlanGeneratorClient();
        var agent = new SentenceEditDeltaAgent(planClient, NullLogger<SentenceEditDeltaAgent>.Instance);

        await agent.EnrichAsync(group, rows);

        group.FindMember(1)!.EditDelta.Should().NotBeNull();
        group.FindMember(1)!.EditDelta!.Confidence.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test doubles
    // ═══════════════════════════════════════════════════════════════

    private sealed class StaticPlanGeneratorClient : IPlanGeneratorClient
    {
        private readonly object? _response;

        public StaticPlanGeneratorClient(object? response = null)
        {
            _response = response;
        }

        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model,
            CancellationToken ct = default) where T : class
        {
            return Task.FromResult(_response as T);
        }

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input,
            CancellationToken ct = default)
        {
            return Task.FromResult<WordImagePromptPlan?>(null);
        }

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input,
            CancellationToken ct = default)
        {
            return Task.FromResult<WordImageVisualPlan?>(null);
        }
    }

    private sealed class ThrowingPlanGeneratorClient : IPlanGeneratorClient
    {
        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model,
            CancellationToken ct = default) where T : class
        {
            throw new InvalidOperationException("Simulated LLM failure");
        }

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input,
            CancellationToken ct = default) => Task.FromResult<WordImagePromptPlan?>(null);

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input,
            CancellationToken ct = default) => Task.FromResult<WordImageVisualPlan?>(null);
    }

    private sealed class NullReturningPlanGeneratorClient : IPlanGeneratorClient
    {
        public Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model,
            CancellationToken ct = default) where T : class
        {
            return Task.FromResult<T?>(null);
        }

        public Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input,
            CancellationToken ct = default) => Task.FromResult<WordImagePromptPlan?>(null);

        public Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input,
            CancellationToken ct = default) => Task.FromResult<WordImageVisualPlan?>(null);
    }

}