using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageBatchOrchestratorTests
{
    // ═══════════════════════════════════════════════════════════════
    // GenerateBatchAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateBatchAsync_IneligibleGroup_ShouldGenerateAllIndependently()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "Be careful", OrderIndex = 1 },
            new() { RowId = 2, English = "Don't run", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "safety_rules", Confidence = 1.0,
            Members = [new() { RowId = 1 }, new() { RowId = 2 }]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        results.All(r => !r.UsedReferenceEdit).Should().BeTrue();
        genClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerateBatchAsync_EligibleGroup_WithValidEditDelta_ShouldUseReferenceEditForSubsequentRows()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var editDelta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "an apple", EditDelta = editDelta }
            ]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        results.First(r => r.RowId == 2).UsedReferenceEdit.Should().BeTrue();
        genClient.CallCount.Should().Be(1);
        editClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GenerateBatchAsync_EligibleGroup_WithNoEditDelta_ShouldFallbackToIndependentGeneration()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is a box", OrderIndex = 2 },
        };
        var noEditDelta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "a box", EditDelta = noEditDelta }
            ]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        // Second row should fall back to independent generation (no-edit delta)
        results.First(r => r.RowId == 2).UsedReferenceEdit.Should().BeFalse();
        genClient.CallCount.Should().Be(2);
        editClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateBatchAsync_EligibleGroup_WithLowConfidenceDelta_ShouldFallbackToIndependentGeneration()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "A cat", OrderIndex = 1 },
            new() { RowId = 2, English = "A dog", OrderIndex = 2 },
        };
        var lowConfidenceDelta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.3,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "cat", To = "dog" }]
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "animal_drill", Confidence = 1.0,
            SceneSetting = "pet store", ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a cat" },
                new() { RowId = 2, VisualFocus = "a dog", EditDelta = lowConfidenceDelta }
            ]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        results.First(r => r.RowId == 2).UsedReferenceEdit.Should().BeFalse();
        genClient.CallCount.Should().Be(2);
        editClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateBatchAsync_EligibleGroup_FirstRowNoUrl_ShouldFallbackAllToIndependentGeneration()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var editDelta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "an apple", EditDelta = editDelta }
            ]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient(returnUrl: false);
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        results.All(r => !r.UsedReferenceEdit).Should().BeTrue();
        genClient.CallCount.Should().Be(2);
        editClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateBatchAsync_WhenReferenceEditFails_ShouldFallbackToIndependentGeneration()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var editDelta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "an apple", EditDelta = editDelta }
            ]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var editClient = new FakeReferenceEditClient(shouldFail: true);
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.Should().HaveCount(2);
        // Second row should have reused source image (fallback on edit failure in orchestrator)
        results.First(r => r.RowId == 2).ReusedSourceImage.Should().BeTrue();
        genClient.CallCount.Should().Be(1);
        editClient.CallCount.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // ImageGenerationOrchestrator - GenerateFromReferenceEditDeltaAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateFromReferenceEditDeltaAsync_WhenDeltaNotEligible_ShouldReuseSourceImage()
    {
        var orchestrator = CreateOrchestrator(editClient: new FakeReferenceEditClient());
        var delta = new SentenceImageEditDelta { RowId = 2, ReferenceRowId = 1, Confidence = 0 };
        var result = await orchestrator.GenerateFromReferenceEditDeltaAsync(delta, "https://cdn.example.com/source.png");
        result.ReusedSourceImage.Should().BeTrue();
        result.ImageResponse.Images[0].Url.Should().Be("https://cdn.example.com/source.png");
        result.ImageResponse.Provider.Should().Be("ReferenceImage");
    }

    [Fact]
    public async Task GenerateFromReferenceEditDeltaAsync_WhenDeltaIsEligible_ShouldCallEditClient()
    {
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateOrchestrator(editClient: editClient);
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };
        var result = await orchestrator.GenerateFromReferenceEditDeltaAsync(delta, "https://cdn.example.com/source.png");
        result.ReusedSourceImage.Should().BeFalse();
        editClient.LastRequest!.ReferenceImageUrl.Should().Be("https://cdn.example.com/source.png");
        editClient.LastRequest.Prompt.Should().Be("Only edit: box -> apple.");
    }

    [Fact]
    public async Task GenerateFromReferenceEditDeltaAsync_WhenNoEditClientRegistered_ShouldThrow()
    {
        var orchestrator = CreateOrchestrator(editClient: null);
        var delta = new SentenceImageEditDelta
        {
            RowId = 2, ReferenceRowId = 1, Confidence = 0.95,
            Operations = [new SentenceImageEditOperation { Operation = "replace", From = "box", To = "apple" }]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.GenerateFromReferenceEditDeltaAsync(delta, "https://cdn.example.com/source.png"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static ImageGenerationOrchestrator CreateOrchestrator(
        IReferenceImageEditClient? editClient = null)
    {
        var pipeline = new FakeWordImagePromptPipeline();
        var refEditClients = editClient is not null ? [editClient] : Array.Empty<IReferenceImageEditClient>();
        return new ImageGenerationOrchestrator(pipeline, new FakeImageGenerationClient(), refEditClients,
            NullLogger<ImageGenerationOrchestrator>.Instance);
    }

    private static SentenceImageBatchOrchestrator CreateBatchOrchestrator(
        ISceneGroupingAgent? groupingAgent = null,
        IAIImageGenerationClient? genClient = null,
        IReferenceImageEditClient? editClient = null)
    {
        var pipeline = new FakeWordImagePromptPipeline();
        var refEditClients = editClient is not null ? [editClient] : Array.Empty<IReferenceImageEditClient>();
        var orchestrator = new ImageGenerationOrchestrator(pipeline, genClient ?? new FakeImageGenerationClient(),
            refEditClients, NullLogger<ImageGenerationOrchestrator>.Instance);
        return new SentenceImageBatchOrchestrator(
            groupingAgent ?? new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [], UncertainRowIds = [] }),
            orchestrator, NullLogger<SentenceImageBatchOrchestrator>.Instance);
    }

    private sealed class FakeWordImagePromptPipeline : WordImagePromptPipeline
    {
        public FakeWordImagePromptPipeline() : base(null!, null!) { }
        public override Task<(string?, string?)> GeneratePromptsAsync(string t, CancellationToken ct = default)
            => Task.FromResult<(string?, string?)>((t, t));
    }

    private sealed class FakeImageGenerationClient : IAIImageGenerationClient
    {
        private readonly bool _returnUrl;
        public int CallCount { get; private set; }
        public FakeImageGenerationClient(bool returnUrl = true) { _returnUrl = returnUrl; }
        public ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest r, CancellationToken ct = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = "fake", Model = "fake",
                Images = [new AIImageData { Url = _returnUrl ? "https://fake.url/gen-image.png" : null }]
            });
        }
    }

    private sealed class FakeReferenceEditClient : IReferenceImageEditClient
    {
        private readonly bool _shouldFail;
        public int CallCount { get; private set; }
        public ReferenceImageEditRequest? LastRequest { get; private set; }
        public FakeReferenceEditClient(bool shouldFail = false) { _shouldFail = shouldFail; }
        public ValueTask<AIImageResponse> EditReferenceAsync(ReferenceImageEditRequest r, CancellationToken ct = default)
        {
            CallCount++; LastRequest = r;
            if (_shouldFail)
                throw new InvalidOperationException("Simulated edit failure");
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = "fake", Model = "fake",
                Images = [new AIImageData { Url = "https://fake.url/edited-image.png" }]
            });
        }
    }

    private sealed class FakeSceneGroupingAgent : ISceneGroupingAgent
    {
        private readonly SceneGroupingResult _result;
        public FakeSceneGroupingAgent(SceneGroupingResult result) { _result = result; }
        public Task<SceneGroupingResult> GroupAsync(IReadOnlyList<WordImageRow> rows, CancellationToken ct = default)
            => Task.FromResult(_result);
    }
}
