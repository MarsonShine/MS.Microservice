using FluentAssertions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class RoutingAICapabilityClientTests
{
    [Fact]
    public async Task SynthesizeAsync_ShouldDelegateToResolvedProvider()
    {
        var ttsProvider = new FakeTtsProvider();
        var client = new RoutingAITtsClient(
            new FakeModelResolver(CreateModel(AICapability.Tts, voice: "alloy")),
            new FakeProviderFactory(ttsProvider: ttsProvider));

        var response = await client.SynthesizeAsync(new AITtsRequest
        {
            Input = "hello",
        });

        response.Provider.Should().Be("OpenAI");
        ttsProvider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RecognizeAsync_ShouldDelegateToResolvedProvider()
    {
        var asrProvider = new FakeAsrProvider();
        var client = new RoutingAIAsrClient(
            new FakeModelResolver(CreateModel(AICapability.Asr)),
            new FakeProviderFactory(asrProvider: asrProvider));

        var response = await client.RecognizeAsync(new AIAsrRequest
        {
            Audio = CreateBinary("audio.wav", "audio/wav"),
        });

        response.Text.Should().Be("transcribed");
        asrProvider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GenerateAsync_ShouldDelegateToResolvedProvider()
    {
        var provider = new FakeImageGenerationProvider();
        var client = new RoutingAIImageGenerationClient(
            new FakeModelResolver(CreateModel(AICapability.ImageGeneration)),
            new FakeProviderFactory(imageGenerationProvider: provider));

        var response = await client.GenerateAsync(new AIImageGenerationRequest
        {
            Prompt = "cat",
        });

        response.Images.Should().ContainSingle();
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task EditAsync_ShouldDelegateToResolvedProvider()
    {
        var provider = new FakeImageEditProvider();
        var client = new RoutingAIImageEditClient(
            new FakeModelResolver(CreateModel(AICapability.ImageEdit)),
            new FakeProviderFactory(imageEditProvider: provider));

        var response = await client.EditAsync(new AIImageEditRequest
        {
            Prompt = "remove background",
            Image = CreateBinary("image.png", "image/png"),
        });

        response.Images.Should().ContainSingle();
        provider.CallCount.Should().Be(1);
    }

    private static AIResolvedModel CreateModel(AICapability capability, string? voice = null)
    {
        return new AIResolvedModel
        {
            Capability = capability,
            Provider = "OpenAI",
            Model = capability switch
            {
                AICapability.Tts => "gpt-4o-mini-tts",
                AICapability.Asr => "whisper-1",
                _ => "gpt-image-1",
            },
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            Voice = voice,
        };
    }

    private static AIBinaryContent CreateBinary(string fileName, string contentType)
    {
        return new AIBinaryContent
        {
            Content = [1, 2, 3],
            ContentType = contentType,
            FileName = fileName,
        };
    }

    private sealed class FakeModelResolver : IAIModelResolver
    {
        private readonly AIResolvedModel _model;

        public FakeModelResolver(AIResolvedModel model)
        {
            _model = model;
        }

        public AIResolvedModel ResolveChatModel(AIChatRequest request) => _model;

        public AIResolvedModel ResolveTtsModel(AITtsRequest request) => _model;

        public AIResolvedModel ResolveAsrModel(AIAsrRequest request) => _model;

        public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request) => _model;

        public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request) => _model;
    }

    private sealed class FakeProviderFactory : IAIProviderFactory
    {
        private readonly IAITtsProvider? _ttsProvider;
        private readonly IAIAsrProvider? _asrProvider;
        private readonly IAIImageGenerationProvider? _imageGenerationProvider;
        private readonly IAIImageEditProvider? _imageEditProvider;

        public FakeProviderFactory(
            IAITtsProvider? ttsProvider = null,
            IAIAsrProvider? asrProvider = null,
            IAIImageGenerationProvider? imageGenerationProvider = null,
            IAIImageEditProvider? imageEditProvider = null)
        {
            _ttsProvider = ttsProvider;
            _asrProvider = asrProvider;
            _imageGenerationProvider = imageGenerationProvider;
            _imageEditProvider = imageEditProvider;
        }

        public IAIChatProvider GetRequiredChatProvider(string providerName) => throw new NotSupportedException();

        public IAITtsProvider GetRequiredTtsProvider(string providerName) => _ttsProvider ?? throw new NotSupportedException();

        public IAIAsrProvider GetRequiredAsrProvider(string providerName) => _asrProvider ?? throw new NotSupportedException();

        public IAIImageGenerationProvider GetRequiredImageGenerationProvider(string providerName) => _imageGenerationProvider ?? throw new NotSupportedException();

        public IAIImageEditProvider GetRequiredImageEditProvider(string providerName) => _imageEditProvider ?? throw new NotSupportedException();
    }

    private sealed class FakeTtsProvider : IAITtsProvider
    {
        public string Name => "OpenAI";

        public int CallCount { get; private set; }

        public ValueTask<AITtsResponse> SynthesizeAsync(AIResolvedModel model, AITtsRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AITtsResponse
            {
                Provider = Name,
                Model = model.Model,
                Voice = model.Voice,
                Audio = CreateBinary("speech.mp3", "audio/mpeg"),
            });
        }
    }

    private sealed class FakeAsrProvider : IAIAsrProvider
    {
        public string Name => "OpenAI";

        public int CallCount { get; private set; }

        public ValueTask<AIAsrResponse> RecognizeAsync(AIResolvedModel model, AIAsrRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AIAsrResponse
            {
                Provider = Name,
                Model = model.Model,
                Text = "transcribed",
            });
        }
    }

    private sealed class FakeImageGenerationProvider : IAIImageGenerationProvider
    {
        public string Name => "OpenAI";

        public int CallCount { get; private set; }

        public ValueTask<AIImageResponse> GenerateAsync(AIResolvedModel model, AIImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = Name,
                Model = model.Model,
                Images = [new AIImageData { Url = "https://example.com/image.png" }],
            });
        }
    }

    private sealed class FakeImageEditProvider : IAIImageEditProvider
    {
        public string Name => "OpenAI";

        public int CallCount { get; private set; }

        public ValueTask<AIImageResponse> EditAsync(AIResolvedModel model, AIImageEditRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new AIImageResponse
            {
                Provider = Name,
                Model = model.Model,
                Images = [new AIImageData { Url = "https://example.com/edited.png" }],
            });
        }
    }
}