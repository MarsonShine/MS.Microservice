using FluentAssertions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Qwen;
using MS.Microservice.Samples.EducationalImages.Models;
using MS.Microservice.Samples.EducationalImages.Qwen;

namespace MS.Microservice.Samples.EducationalImages.Tests;
public sealed class QwenAdapterTests
{    [Fact]
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
