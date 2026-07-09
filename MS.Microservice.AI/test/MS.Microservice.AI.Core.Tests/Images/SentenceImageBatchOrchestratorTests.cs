using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageBatchOrchestratorTests
{
    [Fact]
    public async Task GenerateFromTextWithReferenceAsync_WhenSafePromptIsEmpty_ShouldReuseSourceImage()
    {
        var orchestrator = CreateOrchestrator(referenceEditPromptsResult: (null, string.Empty));
        var result = await orchestrator.GenerateFromTextWithReferenceAsync("apple", "https://cdn.example.com/source.png");
        result.ReusedSourceImage.Should().BeTrue();
        result.ImageResponse.Images[0].Url.Should().Be("https://cdn.example.com/source.png");
        result.ImageResponse.Provider.Should().Be("ReferenceImage");
    }

    [Fact]
    public async Task GenerateFromTextWithReferenceAsync_WhenSafePromptIsNotEmpty_ShouldCallEditClient()
    {
        var editClient = new FakeReferenceEditClient();
        var orchestrator = CreateOrchestrator(
            referenceEditPromptsResult: ("rich prompt", "safe edit prompt"), editClient: editClient);
        var result = await orchestrator.GenerateFromTextWithReferenceAsync(
            "apple (Replace apple with banana)", "https://cdn.example.com/source.png");
        result.ReusedSourceImage.Should().BeFalse();
        editClient.LastRequest!.ReferenceImageUrl.Should().Be("https://cdn.example.com/source.png");
    }

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
    public async Task GenerateBatchAsync_EligibleGroup_ShouldUseReferenceEditForSubsequentRows()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is an apple", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members = [new() { RowId = 1, VisualFocus = "a box" }, new() { RowId = 2, VisualFocus = "an apple" }]
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
    public async Task GenerateBatchAsync_WhenReferenceEditReusesSource_ShouldNotAdvanceReferenceContext()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is a box", OrderIndex = 2 },
        };
        var group = new VisualContextGroup
        {
            GroupId = "G1", RowIds = [1, 2], GroupType = "object_drill", Confidence = 1.0,
            SceneSetting = "classroom table", ContinuityPolicy = "Same characters, same setting",
            Members = [new() { RowId = 1, VisualFocus = "a box" }, new() { RowId = 2, VisualFocus = "a box" }]
        };
        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [group], UncertainRowIds = [] });
        var genClient = new FakeImageGenerationClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient,
            referenceEditPromptsResult: (null, string.Empty));
        var results = await orchestrator.GenerateBatchAsync(rows);
        results.First(r => r.RowId == 2).ReusedSourceImage.Should().BeTrue();
    }

    private static ImageGenerationOrchestrator CreateOrchestrator(
        (string?, string?) referenceEditPromptsResult = default,
        IReferenceImageEditClient? editClient = null)
    {
        var pipeline = new FakeWordImagePromptPipeline(referenceEditPromptsResult);
        var orchestrator = new ImageGenerationOrchestrator(pipeline, new FakeImageGenerationClient(),
            NullLogger<ImageGenerationOrchestrator>.Instance);
        orchestrator.ReferenceEditClient = editClient ?? new FakeReferenceEditClient();
        return orchestrator;
    }

    private static SentenceImageBatchOrchestrator CreateBatchOrchestrator(
        ISceneGroupingAgent? groupingAgent = null,
        IAIImageGenerationClient? genClient = null,
        IReferenceImageEditClient? editClient = null,
        (string?, string?) referenceEditPromptsResult = default)
    {
        var pipeline = new FakeWordImagePromptPipeline(referenceEditPromptsResult);
        var orchestrator = new ImageGenerationOrchestrator(pipeline, genClient ?? new FakeImageGenerationClient(),
            NullLogger<ImageGenerationOrchestrator>.Instance);
        orchestrator.ReferenceEditClient = editClient ?? new FakeReferenceEditClient();
        return new SentenceImageBatchOrchestrator(
            groupingAgent ?? new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [], UncertainRowIds = [] }),
            orchestrator, NullLogger<SentenceImageBatchOrchestrator>.Instance);
    }

    private sealed class FakeWordImagePromptPipeline : WordImagePromptPipeline
    {
        private readonly (string?, string?) _result;
        public FakeWordImagePromptPipeline((string?, string?) result = default) : base(null!, null!) { _result = result; }
        public override Task<(string?, string?)> GeneratePromptsAsync(string t, CancellationToken ct = default)
            => Task.FromResult<(string?, string?)>((t, t));
        public override Task<(string?, string?)> GenerateReferenceEditPromptsAsync(string t, CancellationToken ct = default)
            => Task.FromResult(_result == default ? ((string?, string?))(t, t) : _result);
        public override string GenerateReferenceEditNegativePrompt(string t) => "style transfer, full redraw";
    }

    private sealed class FakeImageGenerationClient : IAIImageGenerationClient
    {
        public int CallCount { get; private set; }
        public ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest r, CancellationToken ct = default)
        { CallCount++; return ValueTask.FromResult(new AIImageResponse { Provider = "fake", Model = "fake", Images = [new AIImageData { Url = "https://fake.url/gen-image.png" }] }); }
    }

    private sealed class FakeReferenceEditClient : IReferenceImageEditClient
    {
        public int CallCount { get; private set; }
        public ReferenceImageEditRequest? LastRequest { get; private set; }
        public ValueTask<AIImageResponse> EditReferenceAsync(ReferenceImageEditRequest r, CancellationToken ct = default)
        { CallCount++; LastRequest = r; return ValueTask.FromResult(new AIImageResponse { Provider = "fake", Model = "fake", Images = [new AIImageData { Url = "https://fake.url/edited-image.png" }] }); }
    }

    private sealed class FakeSceneGroupingAgent : ISceneGroupingAgent
    {
        private readonly SceneGroupingResult _result;
        public FakeSceneGroupingAgent(SceneGroupingResult result) { _result = result; }
        public Task<SceneGroupingResult> GroupAsync(IReadOnlyList<WordImageRow> rows, CancellationToken ct = default)
            => Task.FromResult(_result);
    }
}
