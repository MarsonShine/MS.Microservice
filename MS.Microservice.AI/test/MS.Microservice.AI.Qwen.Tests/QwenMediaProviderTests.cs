using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
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
    public async Task EditAsync_WithReferenceImageUrl_ShouldUseMultimodalGenerationEndpoint()
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
        var provider = CreateImageEditProvider(handler);

        var response = await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "make it sunny",
            ReferenceImageUrl = "https://cdn.example.com/source.png",
            NegativePrompt = "rain, clouds",
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://dashscope.example.com/edited-ref.png");

        // Verify the request went to the multimodal generation endpoint
        var requestUri = handler.Requests[0].RequestUri!.ToString();
        requestUri.Should().Contain("api/v1/services/aigc/multimodal-generation/generation");
    }

    [Fact]
    public async Task EditAsync_WithReferenceImageUrl_ShouldSendJsonWithRequiredFields()
    {
        string? capturedBody = null;
        var handler = new TestHandler(request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
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
            };
        });
        var provider = CreateImageEditProvider(handler);

        await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "change the weather",
            ReferenceImageUrl = "https://cdn.example.com/source.png",
            NegativePrompt = "dark, gloomy",
            Size = "1024x1024",
        });

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        // model
        root.GetProperty("model").GetString().Should().Be("qwen-image");

        // input.messages[0].role = user
        var messages = root.GetProperty("input").GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("user");

        // content = [{image}, {text}]
        var content = messages[0].GetProperty("content");
        content[0].GetProperty("image").GetString().Should().Be("https://cdn.example.com/source.png");

        var text = content[1].GetProperty("text").GetString();
        text.Should().Contain("Use the SOURCE IMAGE as the base canvas");
        text.Should().Contain("change the weather");

        // parameters
        var parameters = root.GetProperty("parameters");
        parameters.GetProperty("negative_prompt").GetString().Should().Be("dark, gloomy");
        parameters.GetProperty("prompt_extend").GetBoolean().Should().BeFalse();
        parameters.GetProperty("watermark").GetBoolean().Should().BeFalse();
        parameters.GetProperty("size").GetString().Should().Be("1024*1024");
    }

    [Fact]
    public async Task EditAsync_WithReferenceImageUrl_ShouldSendSpaceForEmptyNegativePrompt()
    {
        string? capturedBody = null;
        var handler = new TestHandler(request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "output": {
                        "choices": [
                          {
                            "message": {
                              "content": [
                                { "image": "https://example.com/img.png" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var provider = CreateImageEditProvider(handler);

        await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "edit",
            ReferenceImageUrl = "https://example.com/src.png",
            // No NegativePrompt — should send single space
        });

        using var doc = JsonDocument.Parse(capturedBody!);
        var parameters = doc.RootElement.GetProperty("parameters");
        parameters.GetProperty("negative_prompt").GetString().Should().Be(" ");
    }

    [Fact]
    public async Task EditAsync_WithoutReferenceImageUrl_ShouldStillUseMultipartImageEdits()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "data": [
                    {
                      "url": "https://dashscope.example.com/binary-edit.png"
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
            Prompt = "remove background",
            Image = CreateBinary("source.png", "image/png"),
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://dashscope.example.com/binary-edit.png");

        // Should go to the old /images/edits multipart endpoint
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/images/edits");
    }

    [Fact]
    public async Task EditAsync_WithReferenceImageUrl_ShouldParseResponseFromOutputChoices()
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
                            { "image": "https://example.com/img1.png" },
                            { "image": "https://example.com/img2.png" }
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
        var provider = CreateImageEditProvider(handler);

        var response = await provider.EditAsync(
            CreateImageModel(AICapability.ImageEdit) with { Count = 2 },
            new AIImageEditRequest
            {
                Prompt = "edit",
                ReferenceImageUrl = "https://example.com/src.png",
            });

        response.Images.Should().HaveCount(2);
        response.Images[0].Url.Should().Be("https://example.com/img1.png");
        response.Images[1].Url.Should().Be("https://example.com/img2.png");
    }

    [Fact]
    public async Task EditAsync_WithReferenceImageUrl_ShouldMapTopLevelErrorBody()
    {
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """
                {
                  "code": "InvalidParameter",
                  "message": "The parameter 'size' is invalid.",
                  "request_id": "req-abc-123"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateImageEditProvider(handler);

        var act = async () => await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "edit",
            ReferenceImageUrl = "https://example.com/src.png",
        });

        var ex = await act.Should().ThrowAsync<AIProviderException>();
        ex.And.Message.Should().Contain("size"); // the message field from Qwen error body
        ex.And.ProviderRequestId.Should().Be("req-abc-123");
        ex.And.InnerException.Should().NotBeNull();
        ex.And.InnerException!.Message.Should().Contain("InvalidParameter");
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
}