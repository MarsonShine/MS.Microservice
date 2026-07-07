using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Tests;

public sealed class PlanGeneratorClientTests
{
    // ═══════════════════════════════════════════════════════════════
    // ExtractOutputJson
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractOutputJson_ShouldReturnJson_WhenOutputTagPresent()
    {
        var text = "<Output>{\"key\":\"value\"}</Output>";

        var result = PlanGeneratorClient.ExtractOutputJson(text);

        result.Should().Be("{\"key\":\"value\"}");
    }

    [Fact]
    public void ExtractOutputJson_ShouldReturnNull_WhenOutputTagMissing()
    {
        var text = "Some plain text without output tags.";

        var result = PlanGeneratorClient.ExtractOutputJson(text);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOutputJson_ShouldHandleWhitespaceAndNewlines()
    {
        var text = "<Output>\n  {\"a\": 1}\n</Output>";

        var result = PlanGeneratorClient.ExtractOutputJson(text);

        result.Should().Be("{\"a\": 1}");
    }

    [Fact]
    public void ExtractOutputJson_ShouldBeCaseInsensitive()
    {
        var text = "<output>{\"x\":1}</output>";

        var result = PlanGeneratorClient.ExtractOutputJson(text);

        result.Should().Be("{\"x\":1}");
    }

    [Fact]
    public void ExtractOutputJson_ShouldReturnNull_ForEmptyText()
    {
        var result = PlanGeneratorClient.ExtractOutputJson("");

        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // SendAsJsonAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendAsJsonAsync_ShouldDeserialize_WhenValidJsonInOutput()
    {
        var chatClient = new FakeChatClient(
            "<Output>{\"mainSubject\":\"apple\",\"supportingVisual\":\"tree\"}</Output>");
        var client = CreateClient(chatClient);

        var result = await client.SendAsJsonAsync<WordImageVisualPlan>(
            "system", "user", "test-model");

        result.Should().NotBeNull();
        result!.MainSubject.Should().Be("apple");
        result.SupportingVisual.Should().Be("tree");
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldReturnNull_WhenResponseTextEmpty()
    {
        var chatClient = new FakeChatClient("");
        var client = CreateClient(chatClient);

        var result = await client.SendAsJsonAsync<WordImageVisualPlan>(
            "system", "user", "test-model");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldReturnNull_WhenOutputTagMissing()
    {
        var chatClient = new FakeChatClient("plain text response");
        var client = CreateClient(chatClient);

        var result = await client.SendAsJsonAsync<WordImageVisualPlan>(
            "system", "user", "test-model");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldReturnNull_WhenJsonMalformed()
    {
        var chatClient = new FakeChatClient("<Output>{invalid json}</Output>");
        var client = CreateClient(chatClient);

        var result = await client.SendAsJsonAsync<WordImageVisualPlan>(
            "system", "user", "test-model");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldReturnNull_WhenChatClientThrows()
    {
        var chatClient = new FakeChatClient(new InvalidOperationException("API down"));
        var client = CreateClient(chatClient);

        var result = await client.SendAsJsonAsync<WordImageVisualPlan>(
            "system", "user", "test-model");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldPassModelToRequest()
    {
        var chatClient = new FakeChatClient("<Output>{}</Output>");
        var client = CreateClient(chatClient);

        await client.SendAsJsonAsync<WordImageVisualPlan>(
            "sys", "usr", "custom-model");

        chatClient.LastRequest!.Model.Should().Be("custom-model");
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldPassSystemAndUserMessages()
    {
        var chatClient = new FakeChatClient("<Output>{}</Output>");
        var client = CreateClient(chatClient);

        await client.SendAsJsonAsync<WordImageVisualPlan>(
            "sys-prompt", "user-prompt", "model");

        chatClient.LastRequest!.Messages.Should().HaveCount(2);
        chatClient.LastRequest!.Messages[0].Role.Should().Be("system");
        chatClient.LastRequest!.Messages[0].Content.Should().Be("sys-prompt");
        chatClient.LastRequest!.Messages[1].Role.Should().Be("user");
        chatClient.LastRequest!.Messages[1].Content.Should().Be("user-prompt");
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldPropagateCancellation()
    {
        var chatClient = new FakeChatClient(
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new AIChatResponse
                {
                    Provider = "test",
                    Model = "test",
                    Text = "<Output>{}</Output>"
                };
            });
        var client = CreateClient(chatClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await client.SendAsJsonAsync<WordImageVisualPlan>(
            "sys", "usr", "model", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════
    // GenerateAlphabetPlanAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateAlphabetPlanAsync_ShouldApplyPostProcessing()
    {
        var json = JsonSerializer.Serialize(new WordImagePromptPlan
        {
            MainSubject = "ant",
            NegativeElements = ["barefoot", "", "  barefoot  ", "  ", null!, "dot eyes", "beady eyes", "third"]
        });
        var chatClient = new FakeChatClient($"<Output>{json}</Output>");
        var client = CreateClient(chatClient);

        var input = new WordImageInput("A", "A", "letter A", WordImageCardType.Alphabet);
        var plan = await client.GenerateAlphabetPlanAsync(input);

        plan.Should().NotBeNull();
        plan!.AllowVisibleText.Should().BeTrue();
        plan.ReserveTextOverlayArea.Should().BeFalse();
        plan.OverlayText.Should().Be("A");
        plan.NegativeElements.Should().NotBeNull();
        plan.NegativeElements!.Should().HaveCountLessThanOrEqualTo(6);
        plan.NegativeElements.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item));
        plan.NegativeElements.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GenerateAlphabetPlanAsync_ShouldReturnNull_WhenLlmFails()
    {
        var chatClient = new FakeChatClient("");
        var client = CreateClient(chatClient);

        var input = new WordImageInput("B", "B", "letter B", WordImageCardType.Alphabet);
        var plan = await client.GenerateAlphabetPlanAsync(input);

        plan.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // GenerateVisualPlanAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateVisualPlanAsync_ShouldReturnVisualPlan_OnSuccess()
    {
        var json = JsonSerializer.Serialize(new WordImageVisualPlan
        {
            VisualMeaning = "A child running in a classroom",
            MainSubject = "child running",
            SentenceIntent = "prohibition",
            SettingCues = ["desks", "chairs", "windows"]
        });
        var chatClient = new FakeChatClient($"<Output>{json}</Output>");
        var client = CreateClient(chatClient);

        var input = new WordImageInput(
            "Don't run!", "Don't run!", null, WordImageCardType.Sentence);
        var plan = await client.GenerateVisualPlanAsync(input);

        plan.Should().NotBeNull();
        plan!.VisualMeaning.Should().Be("A child running in a classroom");
        plan.SentenceIntent.Should().Be("prohibition");
    }

    [Fact]
    public async Task GenerateVisualPlanAsync_ShouldUseMeaningHint_WhenProvided()
    {
        string? capturedUserMessage = null;
        var chatClient = new FakeChatClient((req, _) =>
        {
            capturedUserMessage = req.Messages[1].Content;
            return new AIChatResponse
            {
                Provider = "test", Model = "test",
                Text = "<Output>{}</Output>"
            };
        });
        var client = CreateClient(chatClient);

        var input = new WordImageInput(
            "apple (fruit)", "apple", "fruit", WordImageCardType.Word);
        await client.GenerateVisualPlanAsync(input);

        // The JSON payload should contain "fruit" as the Meaning field (camelCase from System.Text.Json)
        capturedUserMessage.Should().NotBeNull();
        capturedUserMessage.Should().Contain("\"meaning\":\"fruit\"");
    }

    // ═══════════════════════════════════════════════════════════════
    // ResolveModel / Scenario
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveModel_ShouldReturnNull_ByDefault()
    {
        var client = new TestablePlanGeneratorClient(
            new FakeChatClient("<Output>{}</Output>"),
            NullLogger<PlanGeneratorClient>.Instance);

        client.ResolveModelPublic().Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldDefaultScenario_ToImagePromptPlanning()
    {
        var client = new TestablePlanGeneratorClient(
            new FakeChatClient("<Output>{}</Output>"),
            NullLogger<PlanGeneratorClient>.Instance);

        client.GetScenario().Should().Be(PlanGeneratorClient.DefaultScenario);
    }

    [Fact]
    public void Constructor_ShouldUseProvidedScenario()
    {
        var client = new TestablePlanGeneratorClient(
            new FakeChatClient("<Output>{}</Output>"),
            NullLogger<PlanGeneratorClient>.Instance,
            "MyCustomScenario");

        client.GetScenario().Should().Be("MyCustomScenario");
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldSetScenario_WhenModelIsNull()
    {
        var chatClient = new FakeChatClient("<Output>{}</Output>");
        var client = CreateClient(chatClient, scenario: "TestScenario");

        await client.SendAsJsonAsync<WordImageVisualPlan>(
            "sys", "usr", model: null);

        chatClient.LastRequest!.Scenario.Should().Be("TestScenario");
        chatClient.LastRequest!.Model.Should().BeNull();
    }

    [Fact]
    public async Task SendAsJsonAsync_ShouldSetModel_WhenModelIsProvided()
    {
        var chatClient = new FakeChatClient("<Output>{}</Output>");
        var client = CreateClient(chatClient);

        await client.SendAsJsonAsync<WordImageVisualPlan>(
            "sys", "usr", "direct-model-override");

        chatClient.LastRequest!.Model.Should().Be("direct-model-override");
    }

    // ═══════════════════════════════════════════════════════════════
    // Test doubles
    // ═══════════════════════════════════════════════════════════════

    private static PlanGeneratorClient CreateClient(IAIChatClient chatClient, string scenario = "test-scenario")
    {
        return new PlanGeneratorClient(chatClient, NullLogger<PlanGeneratorClient>.Instance, scenario);
    }

    /// <summary>
    /// Exposes protected members of <see cref="PlanGeneratorClient"/> for testing.
    /// </summary>
    private sealed class TestablePlanGeneratorClient : PlanGeneratorClient
    {
        public TestablePlanGeneratorClient(
            IAIChatClient chatClient, ILogger<PlanGeneratorClient> logger, string scenario = PlanGeneratorClient.DefaultScenario)
            : base(chatClient, logger, scenario) { }

        public string? ResolveModelPublic() => ResolveModel();

        /// <summary>Exposes the scenario stored in the base class.</summary>
        public string GetScenario()
        {
            // Access the private 'scenario' field via reflection or a workaround.
            // Since we control the test, we verify through behavior (SendAsJsonAsync).
            // This is a convenience for constructor-scenario tests.
            return typeof(PlanGeneratorClient)
                .GetField("scenario", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(this) as string ?? string.Empty;
        }
    }

    private sealed class FakeChatClient : IAIChatClient
    {
        private readonly string? _staticResponse;
        private readonly Exception? _exception;
        private readonly Func<AIChatRequest, CancellationToken, AIChatResponse>? _factory;

        public AIChatRequest? LastRequest { get; private set; }

        public FakeChatClient(string staticResponse)
        {
            _staticResponse = staticResponse;
        }

        public FakeChatClient(Exception exception)
        {
            _exception = exception;
        }

        public FakeChatClient(Func<AIChatRequest, CancellationToken, AIChatResponse> factory)
        {
            _factory = factory;
        }

        public ValueTask<AIChatResponse> GetResponseAsync(
            AIChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (_exception is not null)
                throw _exception;

            if (_factory is not null)
                return ValueTask.FromResult(_factory(request, cancellationToken));

            return ValueTask.FromResult(new AIChatResponse
            {
                Provider = "fake",
                Model = request.Model ?? request.Scenario ?? "unknown",
                Text = _staticResponse ?? string.Empty
            });
        }

        public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(
            AIChatRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming not used by PlanGeneratorClient");
    }
}
