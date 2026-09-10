using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.OpenAI;

namespace MS.Microservice.AI.OpenAI.Tests;

public sealed class OpenAIMediaProviderTests
{
    [Fact]
    public async Task SynthesizeAsync_ShouldSendSpeechRequest_AndReturnBinaryAudio()
    {
        var audioBytes = Encoding.UTF8.GetBytes("audio-bytes");
        var handler = new SequenceHttpMessageHandler(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(audioBytes),
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                response.Headers.TryAddWithoutValidation("x-request-id", "tts-request-id");
                return response;
            });

        var provider = CreateTtsProvider(handler);

        var response = await provider.SynthesizeAsync(CreateTtsModel(), new AITtsRequest
        {
            Input = "hello speech",
            RequestId = "req-openai-tts-1",
        });

        response.Provider.Should().Be(OpenAIProviderDefaults.ProviderName);
        response.Audio.Content.Should().Equal(audioBytes);
        response.Audio.ContentType.Should().Be("audio/mpeg");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/audio/speech");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("\"voice\":\"alloy\"");
        body.Should().Contain("\"response_format\":\"mp3\"");
    }

    [Fact]
    public async Task RecognizeAsync_ShouldSendMultipartRequest_AndParseSegments()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "model": "whisper-1",
                  "text": "hello world",
                  "language": "en",
                  "segments": [
                    { "start": 0.0, "end": 1.2, "text": "hello" },
                    { "start": 1.2, "end": 2.4, "text": "world" }
                  ],
                  "usage": {
                    "prompt_tokens": 3,
                    "completion_tokens": 4,
                    "total_tokens": 7
                  }
                }
                """));

        var provider = CreateAsrProvider(handler);

        var response = await provider.RecognizeAsync(CreateAsrModel(), new AIAsrRequest
        {
            Audio = new AIBinaryContent
            {
                Content = Encoding.UTF8.GetBytes("wave"),
                ContentType = "audio/wav",
                FileName = "sample.wav",
            },
            RequestId = "req-openai-asr-1",
        });

        response.Text.Should().Be("hello world");
        response.Language.Should().Be("en");
        response.Segments.Should().HaveCount(2);
        response.Usage.TotalTokens.Should().Be(7);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/audio/transcriptions");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("name=file");
        body.Should().Contain("sample.wav");
        body.Should().Contain("name=model");
        body.Should().Contain("name=response_format");
    }

    [Fact]
    public async Task GenerateAsync_ShouldDecodeBase64ImagePayload()
    {
        var imageBytes = Encoding.UTF8.GetBytes("png-bytes");
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "data": [
                    {
                      "b64_json": "{{Convert.ToBase64String(imageBytes)}}",
                      "revised_prompt": "revised cat"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 9,
                    "completion_tokens": 0,
                    "total_tokens": 9
                  }
                }
                """));

        var provider = CreateImageGenerationProvider(handler);

        var response = await provider.GenerateAsync(CreateImageModel(AICapability.ImageGeneration), new AIImageGenerationRequest
        {
            Prompt = "draw a cat",
            RequestId = "req-openai-img-1",
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Content.Should().NotBeNull();
        response.Images[0].Content!.Content.Should().Equal(imageBytes);
        response.Images[0].RevisedPrompt.Should().Be("revised cat");
        response.Usage.TotalTokens.Should().Be(9);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/images/generations");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("\"response_format\":\"b64_json\"");
    }

    [Fact]
    public async Task EditAsync_ShouldSendMultipartImageAndMask_AndParseUrlResponse()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "url": "https://cdn.example.com/edited.png",
                      "revised_prompt": "cleaned up"
                    }
                  ]
                }
                """));

        var provider = CreateImageEditProvider(handler);

        var response = await provider.EditAsync(CreateImageModel(AICapability.ImageEdit), new AIImageEditRequest
        {
            Prompt = "clean up background",
            Image = new AIBinaryContent
            {
                Content = Encoding.UTF8.GetBytes("image"),
                ContentType = "image/png",
                FileName = "image.png",
            },
            Mask = new AIBinaryContent
            {
                Content = Encoding.UTF8.GetBytes("mask"),
                ContentType = "image/png",
                FileName = "mask.png",
            },
            RequestId = "req-openai-imgedit-1",
        });

        response.Images.Should().ContainSingle();
        response.Images[0].Url.Should().Be("https://cdn.example.com/edited.png");
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/images/edits");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("name=image");
        body.Should().Contain("name=mask");
        body.Should().Contain("name=prompt");
    }

    [Theory]
    [InlineData("mp3", "audio/mpeg")]
    [InlineData("wav", "audio/wav")]
    [InlineData("flac", "audio/flac")]
    [InlineData("aac", "audio/aac")]
    [InlineData("opus", "audio/opus")]
    [InlineData("pcm", "audio/L16")]
    [InlineData("unknown", "application/octet-stream")]
    public async Task SynthesizeAsync_WhenResponseContentTypeIsMissing_ShouldMapAudioContentType(string responseFormat, string expectedContentType)
    {
        var handler = new SequenceHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("audio-bytes")),
        });

        var provider = CreateTtsProvider(handler);

        var response = await provider.SynthesizeAsync(
            CreateTtsModel() with { ResponseFormat = responseFormat },
            new AITtsRequest { Input = "hello speech" });

        response.Audio.ContentType.Should().Be(expectedContentType);
        response.Audio.FileName.Should().Be($"speech.{responseFormat}");
    }

    [Fact]
    public async Task SynthesizeAsync_WhenProviderReturnsContentFilterError_ShouldThrowContentSafetyException()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.BadRequest,
                """
                {
                  "error": {
                    "message": "Content policy triggered",
                    "code": "content_filter"
                  }
                }
                """,
                requestId: "media-filter-id"));

        var provider = CreateTtsProvider(handler);

        Func<Task> action = async () => await provider.SynthesizeAsync(CreateTtsModel(), new AITtsRequest
        {
            Input = "blocked content",
            RequestId = "req-openai-filter",
        });

        var exception = await action.Should().ThrowAsync<AIContentSafetyException>();
        exception.Which.Provider.Should().Be(OpenAIProviderDefaults.ProviderName);
        exception.Which.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        exception.Which.ProviderRequestId.Should().Be("media-filter-id");
    }

    [Fact]
    public async Task SynthesizeAsync_WhenRateLimitedWithRetryAfterDate_ShouldExposeRetryAfter()
    {
        var retryAt = DateTimeOffset.UtcNow.AddSeconds(2);
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.TooManyRequests,
                """
                {
                  "error": {
                    "message": "Too many requests",
                    "code": "rate_limit_reached"
                  }
                }
                """,
                retryAfter: new System.Net.Http.Headers.RetryConditionHeaderValue(retryAt)));

        var provider = CreateTtsProvider(handler);

        Func<Task> action = async () => await provider.SynthesizeAsync(
            CreateTtsModel() with { MaxRetryAttempts = 0 },
            new AITtsRequest { Input = "hello speech" });

        var exception = await action.Should().ThrowAsync<AIRateLimitException>();
        exception.Which.RetryAfter.Should().NotBeNull();
        exception.Which.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RecognizeAsync_WhenTransientProviderErrorOccursBeforeSuccess_ShouldRetryAndReturnParsedResponse()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.ServiceUnavailable,
                """
                {
                  "error": {
                    "message": "Provider overloaded",
                    "code": "server_overloaded"
                  }
                }
                """),
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "model": "whisper-1",
                  "text": "retry success",
                  "language": "en"
                }
                """));

        var provider = CreateAsrProvider(handler);

        var response = await provider.RecognizeAsync(
            CreateAsrModel() with { MaxRetryAttempts = 1 },
            new AIAsrRequest
            {
                Audio = new AIBinaryContent
                {
                    Content = Encoding.UTF8.GetBytes("wave"),
                    ContentType = "audio/wav",
                    FileName = "sample.wav",
                },
            });

        response.Text.Should().Be("retry success");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SynthesizeAsync_WhenTimeoutPersists_ShouldThrowTimeoutException()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => throw new TaskCanceledException("timeout-1"),
            _ => throw new TaskCanceledException("timeout-2"));

        var provider = CreateTtsProvider(handler);

        Func<Task> action = async () => await provider.SynthesizeAsync(
            CreateTtsModel() with { MaxRetryAttempts = 1, Timeout = TimeSpan.FromMilliseconds(1) },
            new AITtsRequest { Input = "hello speech", RequestId = "req-openai-timeout" });

        var exception = await action.Should().ThrowAsync<AITimeoutException>();
        exception.Which.Provider.Should().Be(OpenAIProviderDefaults.ProviderName);
        exception.Which.RequestId.Should().Be("req-openai-timeout");
        handler.Requests.Should().HaveCount(2);
    }

    private static IAITtsProvider CreateTtsProvider(SequenceHttpMessageHandler handler)
    {
        return new OpenAITtsProvider(
            new TestHttpClientFactory(handler),
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<OpenAITtsProvider>.Instance);
    }

    private static IAIAsrProvider CreateAsrProvider(SequenceHttpMessageHandler handler)
    {
        return new OpenAIAsrProvider(
            new TestHttpClientFactory(handler),
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<OpenAIAsrProvider>.Instance);
    }

    private static IAIImageGenerationProvider CreateImageGenerationProvider(SequenceHttpMessageHandler handler)
    {
        return new OpenAIImageGenerationProvider(
            new TestHttpClientFactory(handler),
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<OpenAIImageGenerationProvider>.Instance);
    }

    private static IAIImageEditProvider CreateImageEditProvider(SequenceHttpMessageHandler handler)
    {
        return new OpenAIImageEditProvider(
            new TestHttpClientFactory(handler),
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<OpenAIImageEditProvider>.Instance);
    }

    private static AIOptions CreateOptions()
    {
        var options = new AIOptions();
        options.Providers.Add(OpenAIProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "openai-key",
            BaseAddress = OpenAIProviderDefaults.DefaultBaseAddress,
            TimeoutSeconds = 10,
            MaxRetryAttempts = 1,
            ConcurrencyLimit = 2,
        });

        return options;
    }

    private static AIResolvedModel CreateTtsModel()
    {
        return new AIResolvedModel
        {
            Capability = AICapability.Tts,
            Provider = OpenAIProviderDefaults.ProviderName,
            Model = "gpt-4o-mini-tts",
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            MaxRetryAttempts = 1,
            Voice = "alloy",
            ResponseFormat = "mp3",
        };
    }

    private static AIResolvedModel CreateAsrModel()
    {
        return new AIResolvedModel
        {
            Capability = AICapability.Asr,
            Provider = OpenAIProviderDefaults.ProviderName,
            Model = "whisper-1",
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            MaxRetryAttempts = 0,
            ResponseFormat = "verbose_json",
        };
    }

    private static AIResolvedModel CreateImageModel(AICapability capability)
    {
        return new AIResolvedModel
        {
            Capability = capability,
            Provider = OpenAIProviderDefaults.ProviderName,
            Model = "gpt-image-1",
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            MaxRetryAttempts = 0,
            ResponseFormat = capability == AICapability.ImageGeneration ? "b64_json" : "url",
            Count = 1,
            Size = "1024x1024",
        };
    }

    private static HttpResponseMessage CreateJsonResponse(
        HttpStatusCode statusCode,
        string body,
        string requestId = "openai-media-id",
        System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = retryAfter;
        }

        response.Headers.TryAddWithoutValidation("x-request-id", requestId);
        return response;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler, disposeHandler: false);
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public SequenceHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            var responseFactory = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("No response configured for the test request.");
            return Task.FromResult(responseFactory(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
            }

            return clone;
        }
    }
}
