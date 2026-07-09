using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests.Images;

public sealed class SentenceImageBatchOrchestratorTests
{
    // ── Single reference edit — no delta → reuse source ──

    [Fact]
    public async Task GenerateFromTextWithReferenceAsync_WhenSafePromptIsEmpty_ShouldReuseSourceImage()
    {
        var orchestrator = CreateOrchestrator(
            referenceEditPromptsResult: (null, string.Empty)); // empty safe prompt = no delta

        var result = await orchestrator.GenerateFromTextWithReferenceAsync(
            "apple",
            "https://cdn.example.com/source.png");

        result.ReusedSourceImage.Should().BeTrue();
        result.ImageResponse.Images[0].Url.Should().Be("https://cdn.example.com/source.png");
    }

    // ── Single reference edit — has delta → calls edit client ──

    [Fact]
    public async Task GenerateFromTextWithReferenceAsync_WhenSafePromptIsNotEmpty_ShouldCallEditClient()
    {
        var editClient = new FakeImageEditClient();
        var orchestrator = CreateOrchestrator(
            referenceEditPromptsResult: ("rich prompt", "safe edit prompt"),
            editClient: editClient);

        var result = await orchestrator.GenerateFromTextWithReferenceAsync(
            "apple (Replace apple with banana)",
            "https://cdn.example.com/source.png");

        result.ReusedSourceImage.Should().BeFalse();
        result.SafePrompt.Should().Be("safe edit prompt");
        editClient.LastRequest.Should().NotBeNull();
        editClient.LastRequest!.ReferenceImageUrl.Should().Be("https://cdn.example.com/source.png");
        editClient.LastRequest.NegativePrompt.Should().NotBeNullOrWhiteSpace();
    }

    // ── Batch: ineligible group → all independent generation ──

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
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = "safety_rules", // ineligible
            Confidence = 1.0,
            Members =
            [
                new() { RowId = 1, VisualFocus = "Be careful" },
                new() { RowId = 2, VisualFocus = "Don't run" },
            ]
        };

        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult
        {
            Groups = [group],
            UncertainRowIds = []
        });

        var genClient = new FakeImageGenerationClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient);

        var results = await orchestrator.GenerateBatchAsync(rows);

        results.Should().HaveCount(2);
        results.All(r => !r.UsedReferenceEdit).Should().BeTrue();
        genClient.CallCount.Should().Be(2);
    }

    // ── Batch: eligible group → first row generate, second row reference edit ──

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
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = "object_drill", // eligible
            Confidence = 1.0,
            SceneSetting = "classroom table",
            ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "an apple" },
            ]
        };

        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult
        {
            Groups = [group],
            UncertainRowIds = []
        });

        var genClient = new FakeImageGenerationClient();
        var editClient = new FakeImageEditClient();
        var orchestrator = CreateBatchOrchestrator(groupingAgent, genClient: genClient, editClient: editClient);

        var results = await orchestrator.GenerateBatchAsync(rows);

        results.Should().HaveCount(2);

        // First row: normal generation
        var first = results.First(r => r.RowId == 1);
        first.UsedReferenceEdit.Should().BeFalse();
        first.ReusedSourceImage.Should().BeFalse();

        // Second row: reference edit
        var second = results.First(r => r.RowId == 2);
        second.UsedReferenceEdit.Should().BeTrue();
        second.ReferenceImageUrl.Should().NotBeNull();

        genClient.CallCount.Should().Be(1); // only first row
        editClient.CallCount.Should().Be(1); // second row
    }

    // ── Batch: reference edit reused → does not advance reference context ──

    [Fact]
    public async Task GenerateBatchAsync_WhenReferenceEditReusesSource_ShouldNotAdvanceReferenceContext()
    {
        var rows = new List<WordImageRow>
        {
            new() { RowId = 1, English = "This is a box", OrderIndex = 1 },
            new() { RowId = 2, English = "This is a box", OrderIndex = 2 }, // same sentence → empty delta
        };

        var group = new VisualContextGroup
        {
            GroupId = "G1",
            RowIds = [1, 2],
            GroupType = "object_drill",
            Confidence = 1.0,
            SceneSetting = "classroom table",
            ContinuityPolicy = "Same characters, same setting",
            Members =
            [
                new() { RowId = 1, VisualFocus = "a box" },
                new() { RowId = 2, VisualFocus = "a box" }, // same visual focus
            ]
        };

        var groupingAgent = new FakeSceneGroupingAgent(new SceneGroupingResult
        {
            Groups = [group],
            UncertainRowIds = []
        });

        var genClient = new FakeImageGenerationClient();
        // Edit client that returns "reused" (no actual edit)
        var reusedEditClient = new FakeImageEditClient(reuseSource: true);
        var orchestrator = CreateBatchOrchestrator(
            groupingAgent,
            genClient: genClient,
            editClient: reusedEditClient,
            referenceEditPromptsResult: (null, string.Empty)); // empty safe prompt → reuse

        var results = await orchestrator.GenerateBatchAsync(rows);

        var second = results.First(r => r.RowId == 2);
        second.ReusedSourceImage.Should().BeTrue();
        second.UsedReferenceEdit.Should().BeFalse();

        // Edit client should still have been invoked (but returned reused)
        // Actually, if safe prompt is empty, GenerateFromTextWithReferenceAsync doesn't call edit client.
        // The ReferenceImageUrl on the result should be the original source.
        second.ReferenceImageUrl.Should().Be("https://fake.url/gen-image.png");
    }

    // ── Helpers ──

    private static ImageGenerationOrchestrator CreateOrchestrator(
        (string?, string?) referenceEditPromptsResult = default,
        IAIImageEditClient? editClient = null)
    {
        var pipeline = new FakeWordImagePromptPipeline(referenceEditPromptsResult);
        var genClient = new FakeImageGenerationClient();
        return new ImageGenerationOrchestrator(
            pipeline,
            genClient,
            editClient ?? new FakeImageEditClient(),
            NullLogger<ImageGenerationOrchestrator>.Instance);
    }

    private static SentenceImageBatchOrchestrator CreateBatchOrchestrator(
        ISceneGroupingAgent? groupingAgent = null,
        IAIImageGenerationClient? genClient = null,
        IAIImageEditClient? editClient = null,
        (string?, string?) referenceEditPromptsResult = default)
    {
        var pipeline = new FakeWordImagePromptPipeline(referenceEditPromptsResult);
        var orchestrator = new ImageGenerationOrchestrator(
            pipeline,
            genClient ?? new FakeImageGenerationClient(),
            editClient ?? new FakeImageEditClient(),
            NullLogger<ImageGenerationOrchestrator>.Instance);

        return new SentenceImageBatchOrchestrator(
            groupingAgent ?? new FakeSceneGroupingAgent(new SceneGroupingResult { Groups = [], UncertainRowIds = [] }),
            orchestrator,
            NullLogger<SentenceImageBatchOrchestrator>.Instance);
    }

    // ── Test doubles ──

    private sealed class FakeWordImagePromptPipeline : WordImagePromptPipeline
    {
        private readonly (string?, string?) _referenceEditPromptsResult;

        public FakeWordImagePromptPipeline((string?, string?) referenceEditPromptsResult = default)
            : base(null!, null!)
        {
            _referenceEditPromptsResult = referenceEditPromptsResult;
        }

        public override Task<(string? RichPrompt, string? SafePrompt)> GeneratePromptsAsync(
            string wordText, CancellationToken ct = default)
        {
            return Task.FromResult<(string?, string?)>((wordText, wordText));
        }

        public override Task<(string? RichPrompt, string? SafePrompt)> GenerateReferenceEditPromptsAsync(
            string wordText, CancellationToken ct = default)
        {
            if (_referenceEditPromptsResult == default)
                return Task.FromResult<(string?, string?)>((wordText, wordText));

            return Task.FromResult(_referenceEditPromptsResult);
        }

        public override string GenerateReferenceEditNegativePrompt(string wordText)
        {
            return "style transfer, full redraw";
        }
    }

    private sealed class FakeImageGenerationClient : IAIImageGenerationClient
    {
        public int CallCount { get; private set; }

        public ValueTask<AIImageResponse> GenerateAsync(
            AIImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = "fake",
                Model = "fake",
                Images = [new AIImageData { Url = "https://fake.url/gen-image.png" }]
            });
        }
    }

    private sealed class FakeImageEditClient : IAIImageEditClient
    {
        private readonly bool _reuseSource;
        public int CallCount { get; private set; }
        public AIImageEditRequest? LastRequest { get; private set; }

        public FakeImageEditClient(bool reuseSource = false)
        {
            _reuseSource = reuseSource;
        }

        public ValueTask<AIImageResponse> EditAsync(
            AIImageEditRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            if (_reuseSource && !string.IsNullOrWhiteSpace(request.ReferenceImageUrl))
            {
                return ValueTask.FromResult(new AIImageResponse
                {
                    Provider = "fake",
                    Model = "fake",
                    Images = [new AIImageData { Url = request.ReferenceImageUrl }]
                });
            }

            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = "fake",
                Model = "fake",
                Images = [new AIImageData { Url = "https://fake.url/edited-image.png" }]
            });
        }
    }

    private sealed class FakeSceneGroupingAgent : ISceneGroupingAgent
    {
        private readonly SceneGroupingResult _result;

        public FakeSceneGroupingAgent(SceneGroupingResult result)
        {
            _result = result;
        }

        public Task<SceneGroupingResult> GroupAsync(IReadOnlyList<WordImageRow> rows, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
