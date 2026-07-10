using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.AI.Qwen;

namespace MS.Microservice.AI.Qwen.Tests;

public sealed class QwenMediaProviderTests
{
    [Fact]
    public async Task SynthesizeAsync_ShouldUseQwenCompatibleSpeechEndpoint()
    {
        var handler = new TestHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("speech")),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            return response;
        });
        var provider = CreateTtsProvider(handler);

        var response = await provider.SynthesizeAsync(CreateTtsModel(), new AITtsRequest
        {
            Input = "你好",
        });

        response.Audio.Content.Should().Equal(Encoding.UTF8.GetBytes("speech"));
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/audio/speech");
    }

    [Fact]
    public async Task RecognizeAsync_ShouldUseQwenCompatibleTranscriptionEndpoint()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "text": "语音转文字",
                  "language": "zh"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateAsrProvider(handler);

        var response = await provider.RecognizeAsync(CreateAsrModel(), new AIAsrRequest
        {
            Audio = CreateBinary("sample.wav", "audio/wav"),
        });

        response.Text.Should().Be("语音转文字");
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/audio/transcriptions");
    }

    [Fact]
    public async Task GenerateAsync_ShouldUseQwenCompatibleImageGenerationEndpoint()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "data": [
                    {
                      "b64_json": "{{Convert.ToBase64String(Encoding.UTF8.GetBytes("qwen-image"))}}"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateImageGenerationProvider(handler);

        var response = await provider.GenerateAsync(CreateImageModel(AICapability.ImageGeneration), new AIImageGenerationRequest
        {
            Prompt = "一只猫",
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Content.Should().NotBeNull();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/images/generations");
    }

    [Fact]
    public async Task EditAsync_ShouldUseQwenCompatibleImageEditEndpoint()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "data": [
                    {
                      "url": "https://dashscope.example.com/edited.png"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateImageEditProvider(handler);

        var response = await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "擦除背景",
            Image = CreateBinary("source.png", "image/png"),
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://dashscope.example.com/edited.png");
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/images/edits");
    }

    // ── Reference-image edit (multimodal) tests ──

    [Fact]
    public async Task EditReferenceAsync_ShouldUseMultimodalGenerationEndpoint()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "output": {
                    "choices": [
                      {
                        "message": {
                          "content": [
                            { "image": "https://dashscope.example.com/edited-ref.png" }
                          ]
                        }
                      }
                    ]
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateImageEditProviderInstance(handler);

        var response = await provider.EditReferenceAsync(CreateImageModel(AICapability.ImageEdit), new QwenImageReferenceEditRequest
        {
            Prompt = "make it sunny",
            ReferenceImageUrl = "https://cdn.example.com/source.png",
            NegativePrompt = "rain, clouds",
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://dashscope.example.com/edited-ref.png");

        var requestUri = handler.Requests[0].RequestUri!.ToString();
        requestUri.Should().Contain("api/v1/services/aigc/multimodal-generation/generation");
    }

    [Fact]
    public async Task EditReferenceAsync_ShouldSendJsonWithRequiredFields()
    {
        string? capturedBody = null;
        var handler = new TestHandler(request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"output":{"choices":[{"message":{"content":[{"image":"https://example.com/img.png"}]}}]}}""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var provider = CreateImageEditProviderInstance(handler);

        await provider.EditReferenceAsync(CreateImageModel(AICapability.ImageEdit), new QwenImageReferenceEditRequest
        {
            Prompt = "change the weather",
            ReferenceImageUrl = "https://cdn.example.com/source.png",
            NegativePrompt = "dark, gloomy",
            Size = "1024x1024",
        });

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        root.GetProperty("model").GetString().Should().Be("qwen-image");
        var content = root.GetProperty("input").GetProperty("messages")[0].GetProperty("content");
        content[0].GetProperty("image").GetString().Should().Be("https://cdn.example.com/source.png");
        var text = content[1].GetProperty("text").GetString();
        text.Should().Contain("Use the SOURCE IMAGE as the base canvas");
        text.Should().Contain("change the weather");

        var parameters = root.GetProperty("parameters");
        parameters.GetProperty("negative_prompt").GetString().Should().Be("dark, gloomy");
        parameters.GetProperty("prompt_extend").GetBoolean().Should().BeFalse();
        parameters.GetProperty("watermark").GetBoolean().Should().BeFalse();
        parameters.GetProperty("size").GetString().Should().Be("1024*1024");
    }

    [Fact]
    public async Task EditReferenceAsync_ShouldSendSpaceForEmptyNegativePrompt()
    {
        string? capturedBody = null;
        var handler = new TestHandler(request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"output":{"choices":[{"message":{"content":[{"image":"https://example.com/img.png"}]}}]}}""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var provider = CreateImageEditProviderInstance(handler);

        await provider.EditReferenceAsync(CreateImageModel(AICapability.ImageEdit), new QwenImageReferenceEditRequest
        {
            Prompt = "edit",
            ReferenceImageUrl = "https://example.com/src.png",
        });

        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("parameters").GetProperty("negative_prompt").GetString().Should().Be(" ");
    }

    [Fact]
    public async Task EditAsync_WithoutReferenceImageUrl_ShouldStillUseMultipartImageEdits()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"url":"https://dashscope.example.com/binary-edit.png"}]}""",
                Encoding.UTF8, "application/json"),
        });
        var provider = CreateImageEditProvider(handler);

        var response = await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "remove background",
            Image = CreateBinary("source.png", "image/png"),
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://dashscope.example.com/binary-edit.png");
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/images/edits");
    }

    [Fact]
    public async Task EditReferenceAsync_ShouldParseResponseFromOutputChoices()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"output":{"choices":[{"message":{"content":[{"image":"https://example.com/img1.png"},{"image":"https://example.com/img2.png"}]}}]}}""",
                Encoding.UTF8, "application/json"),
        });
        var provider = CreateImageEditProviderInstance(handler);

        var response = await provider.EditReferenceAsync(
            CreateImageModel(AICapability.ImageEdit) with { Count = 2 },
            new QwenImageReferenceEditRequest { Prompt = "edit", ReferenceImageUrl = "https://example.com/src.png", Count = 2 });

        response.Images.Should().HaveCount(2);
        response.Images[0].Url.Should().Be("https://example.com/img1.png");
        response.Images[1].Url.Should().Be("https://example.com/img2.png");
    }

    [Fact]
    public async Task EditReferenceAsync_ShouldMapTopLevelErrorBody()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"code":"InvalidParameter","message":"The parameter 'size' is invalid.","request_id":"req-abc-123"}""",
                Encoding.UTF8, "application/json"),
        });
        var provider = CreateImageEditProviderInstance(handler);

        var act = async () => await provider.EditReferenceAsync(CreateImageModel(AICapability.ImageEdit),
            new QwenImageReferenceEditRequest { Prompt = "edit", ReferenceImageUrl = "https://example.com/src.png" });

        var ex = await act.Should().ThrowAsync<AIProviderException>();
        ex.And.Message.Should().Contain("size");
        ex.And.ProviderRequestId.Should().Be("req-abc-123");
        ex.And.InnerException.Should().NotBeNull();
        ex.And.InnerException!.Message.Should().Contain("InvalidParameter");
    }

    [Fact]
    public async Task QwenReferenceImageEditAdapter_ShouldMapRequestCorrectly()
    {
        QwenImageReferenceEditRequest? capturedRequest = null;
        var fakeClient = new FakeQwenReferenceEditClient(request =>
        {
            capturedRequest = request;
            return new AIImageResponse
            {
                Provider = "Qwen", Model = "qwen-image-edit-plus",
                Images = [new AIImageData { Url = "https://example.com/edited.png" }]
            };
        });
        var adapter = new QwenReferenceImageEditAdapter(fakeClient);

        var request = new ReferenceImageEditRequest
        {
            Prompt = "Only edit: box -> apple.",
            ReferenceImageUrl = "https://cdn.example.com/source.png",
            NegativePrompt = "style transfer, full redraw",
            Model = "qwen-image-edit-plus",
            Scenario = "QwenReferenceEdit",
            RequestId = "req-123",
            Count = 2,
            Size = "1024x1024",
            Timeout = TimeSpan.FromSeconds(30),
        };

        var response = await adapter.EditReferenceAsync(request);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Prompt.Should().Be("Only edit: box -> apple.");
        capturedRequest.ReferenceImageUrl.Should().Be("https://cdn.example.com/source.png");
        capturedRequest.NegativePrompt.Should().Be("style transfer, full redraw");
        capturedRequest.Model.Should().Be("qwen-image-edit-plus");
        capturedRequest.Scenario.Should().Be("QwenReferenceEdit");
        capturedRequest.RequestId.Should().Be("req-123");
        capturedRequest.Count.Should().Be(2);
        capturedRequest.Size.Should().Be("1024x1024");
        capturedRequest.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://example.com/edited.png");
    }

    private static IAITtsProvider CreateTtsProvider(TestHandler handler)
    {
        return new QwenTtsProvider(new TestHttpClientFactory(handler), Options.Create(CreateOptions()), TimeProvider.System, NullLogger<QwenTtsProvider>.Instance);
    }

    private static IAIAsrProvider CreateAsrProvider(TestHandler handler)
    {
        return new QwenAsrProvider(new TestHttpClientFactory(handler), Options.Create(CreateOptions()), TimeProvider.System, NullLogger<QwenAsrProvider>.Instance);
    }

    private static IAIImageGenerationProvider CreateImageGenerationProvider(TestHandler handler)
    {
        return new QwenImageGenerationProvider(new TestHttpClientFactory(handler), Options.Create(CreateOptions()), TimeProvider.System, NullLogger<QwenImageGenerationProvider>.Instance);
    }

    private static IAIImageEditProvider CreateImageEditProvider(TestHandler handler)
    {
        return new QwenImageEditProvider(new TestHttpClientFactory(handler), Options.Create(CreateOptions()), TimeProvider.System, NullLogger<QwenImageEditProvider>.Instance);
    }

    private static QwenImageEditProvider CreateImageEditProviderInstance(TestHandler handler)
    {
        return new QwenImageEditProvider(new TestHttpClientFactory(handler), Options.Create(CreateOptions()), TimeProvider.System, NullLogger<QwenImageEditProvider>.Instance);
    }

    private static AIOptions CreateOptions()
    {
        var options = new AIOptions();
        options.Providers.Add(QwenProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "qwen-key",
            TimeoutSeconds = 10,
            MaxRetryAttempts = 0,
            ConcurrencyLimit = 2,
        });
        return options;
    }

    private static AIBinaryContent CreateBinary(string fileName, string contentType)
    {
        return new AIBinaryContent
        {
            Content = Encoding.UTF8.GetBytes("binary"),
            ContentType = contentType,
            FileName = fileName,
        };
    }

    private static AIResolvedModel CreateTtsModel() => new()
    {
        Capability = AICapability.Tts,
        Provider = QwenProviderDefaults.ProviderName,
        Model = "qwen-tts",
        Scenario = "Default",
        Timeout = TimeSpan.FromSeconds(10),
        Voice = "chelsie",
        ResponseFormat = "mp3",
    };

    private static AIResolvedModel CreateAsrModel() => new()
    {
        Capability = AICapability.Asr,
        Provider = QwenProviderDefaults.ProviderName,
        Model = "qwen-asr",
        Scenario = "Default",
        Timeout = TimeSpan.FromSeconds(10),
        ResponseFormat = "json",
    };

    private static AIResolvedModel CreateImageModel(AICapability capability) => new()
    {
        Capability = capability,
        Provider = QwenProviderDefaults.ProviderName,
        Model = "qwen-image",
        Scenario = "Default",
        Timeout = TimeSpan.FromSeconds(10),
        ResponseFormat = capability == AICapability.ImageGeneration ? "b64_json" : "url",
        Count = 1,
    };

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler, disposeHandler: false);
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class FakeQwenReferenceEditClient : IQwenImageReferenceEditClient
    {
        private readonly Func<QwenImageReferenceEditRequest, AIImageResponse> _handler;

        public FakeQwenReferenceEditClient(Func<QwenImageReferenceEditRequest, AIImageResponse> handler)
        {
            _handler = handler;
        }

        public ValueTask<AIImageResponse> EditReferenceAsync(QwenImageReferenceEditRequest request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_handler(request));
        }
    }
}