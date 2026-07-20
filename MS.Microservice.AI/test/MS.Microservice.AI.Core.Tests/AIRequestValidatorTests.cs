using System;
using FluentAssertions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using Xunit;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIRequestValidatorTests
{
    [Fact]
    public void ValidateChatRequest_ShouldRejectStructuredFormatForStreaming()
    {
        var request = new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            ResponseFormat = AIChatResponseFormat.JsonObject,
        };

        Action action = () => AIRequestValidator.ValidateChatRequest(
            request,
            isStreaming: true);

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*not supported for streaming*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenMessagesAreEmpty()
    {
        var request = new AIChatRequest { Messages = [] };

        Action action = () => AIRequestValidator.ValidateChatRequest(request);

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*at least one message*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenMessageRoleIsEmpty()
    {
        var request = new AIChatRequest { Messages = [new AIChatMessage("", "hello")] };

        Action action = () => AIRequestValidator.ValidateChatRequest(request);

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*index 0*role*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenMessageContentIsEmpty()
    {
        var request = new AIChatRequest { Messages = [new AIChatMessage("user", "")] };

        Action action = () => AIRequestValidator.ValidateChatRequest(request);

        action.Should().Throw<AIConfigurationException>()
            .WithMessage("*index 0*content*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenCharacterLimitIsExceeded()
    {
        var request = new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "abcd")]
        };
        var limits = new AIPayloadLimitOptions { MaxChatCharacters = 3, MaxStreamingChatCharacters = 2 };

        Action normal = () => AIRequestValidator.ValidateChatRequest(request, limits);
        Action streaming = () => AIRequestValidator.ValidateChatRequest(request, limits, isStreaming: true);

        normal.Should().Throw<AIConfigurationException>().WithMessage("*configured limit*");
        streaming.Should().Throw<AIConfigurationException>().WithMessage("*configured limit*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenNumericFieldsAreInvalid()
    {
        var request = new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            Temperature = -0.1
        };

        Action temperature = () => AIRequestValidator.ValidateChatRequest(request);
        temperature.Should().Throw<AIConfigurationException>().WithMessage("*temperature*");

        request = request with { Temperature = 0.5, TopP = 1.1 };
        Action topP = () => AIRequestValidator.ValidateChatRequest(request);
        topP.Should().Throw<AIConfigurationException>().WithMessage("*top_p*");

        request = request with { TopP = 0.5, MaxOutputTokens = 0 };
        Action maxOutput = () => AIRequestValidator.ValidateChatRequest(request);
        maxOutput.Should().Throw<AIConfigurationException>().WithMessage("*max output tokens*");

        request = request with { MaxOutputTokens = 16, Timeout = TimeSpan.Zero };
        Action timeout = () => AIRequestValidator.ValidateChatRequest(request);
        timeout.Should().Throw<AIConfigurationException>().WithMessage("*timeout*");
    }

    [Fact]
    public void ValidateChatRequest_ShouldThrow_WhenOptionalStringsAreWhitespace()
    {
        var request = new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            Provider = " "
        };

        Action provider = () => AIRequestValidator.ValidateChatRequest(request);
        provider.Should().Throw<AIConfigurationException>().WithMessage("*provider*");

        request = request with { Provider = "openai", Model = " " };
        Action model = () => AIRequestValidator.ValidateChatRequest(request);
        model.Should().Throw<AIConfigurationException>().WithMessage("*model*");

        request = request with { Model = "gpt-4o-mini", Scenario = " " };
        Action scenario = () => AIRequestValidator.ValidateChatRequest(request);
        scenario.Should().Throw<AIConfigurationException>().WithMessage("*scenario*");
    }

    [Fact]
    public void ValidateTtsRequest_ShouldThrow_WhenFieldsAreInvalid()
    {
        var limits = new AIPayloadLimitOptions { MaxTextCharacters = 2 };
        var request = new AITtsRequest { Input = "abc" };

        Action inputLimit = () => AIRequestValidator.ValidateTtsRequest(request, limits);
        inputLimit.Should().Throw<AIConfigurationException>().WithMessage("*configured limit*");

        request = new AITtsRequest { Input = "ok", Voice = " " };
        Action voice = () => AIRequestValidator.ValidateTtsRequest(request);
        voice.Should().Throw<AIConfigurationException>().WithMessage("*voice*");

        request = request with { Voice = "alloy", ResponseFormat = " " };
        Action format = () => AIRequestValidator.ValidateTtsRequest(request);
        format.Should().Throw<AIConfigurationException>().WithMessage("*response format*");

        request = request with { ResponseFormat = "mp3", Speed = 0 };
        Action speed = () => AIRequestValidator.ValidateTtsRequest(request);
        speed.Should().Throw<AIConfigurationException>().WithMessage("*speed*");
    }

    [Fact]
    public void ValidateAsrRequest_ShouldThrow_WhenAudioIsMissingOrTooLarge()
    {
        var emptyAudio = new AIBinaryContent { Content = [], ContentType = "audio/wav" };
        var request = new AIAsrRequest { Audio = emptyAudio };

        Action missing = () => AIRequestValidator.ValidateAsrRequest(request);
        missing.Should().Throw<AIConfigurationException>().WithMessage("*binary content*");

        var largeAudio = new AIBinaryContent { Content = new byte[5], ContentType = "audio/wav" };
        request = new AIAsrRequest { Audio = largeAudio };
        var limits = new AIPayloadLimitOptions { MaxAudioBytes = 4 };

        Action tooLarge = () => AIRequestValidator.ValidateAsrRequest(request, limits);
        tooLarge.Should().Throw<AIConfigurationException>().WithMessage("*configured limit*");
    }

    [Fact]
    public void ValidateImageGenerationRequest_ShouldThrow_WhenFieldsAreInvalid()
    {
        var request = new AIImageGenerationRequest { Prompt = " ", Count = 1 };

        Action prompt = () => AIRequestValidator.ValidateImageGenerationRequest(request);
        prompt.Should().Throw<AIConfigurationException>().WithMessage("*include a prompt*");

        request = new AIImageGenerationRequest { Prompt = "draw", Count = 0 };
        Action count = () => AIRequestValidator.ValidateImageGenerationRequest(request);
        count.Should().Throw<AIConfigurationException>().WithMessage("*count*");

        request = request with { Count = 1, Size = " " };
        Action size = () => AIRequestValidator.ValidateImageGenerationRequest(request);
        size.Should().Throw<AIConfigurationException>().WithMessage("*size*");
    }

    [Fact]
    public void ValidateImageEditRequest_ShouldThrow_WhenPromptIsEmpty()
    {
        var request = new AIImageEditRequest
        {
            Prompt = " ",
            Image = new AIBinaryContent { Content = [1], ContentType = "image/png" },
        };

        Action prompt = () => AIRequestValidator.ValidateImageEditRequest(request);
        prompt.Should().Throw<AIConfigurationException>().WithMessage("*include a prompt*");
    }

    [Fact]
    public void ValidateImageEditRequest_ShouldPass_WhenBinaryImageIsProvided()
    {
        var request = new AIImageEditRequest
        {
            Prompt = "edit",
            Image = new AIBinaryContent { Content = [1, 2, 3], ContentType = "image/png" },
        };

        Action action = () => AIRequestValidator.ValidateImageEditRequest(request);
        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateImageEditRequest_ShouldThrow_WhenBinaryImageIsEmpty()
    {
        var request = new AIImageEditRequest
        {
            Prompt = "edit",
            Image = new AIBinaryContent { Content = [], ContentType = "image/png" },
        };

        Action image = () => AIRequestValidator.ValidateImageEditRequest(request);
        image.Should().Throw<AIConfigurationException>().WithMessage("*binary content*");
    }

    [Fact]
    public void ValidateImageEditRequest_ShouldThrow_WhenBinaryImageExceedsLimit()
    {
        var request = new AIImageEditRequest
        {
            Prompt = "edit",
            Image = new AIBinaryContent { Content = [1, 2, 3, 4, 5], ContentType = "image/png" },
        };
        var limits = new AIPayloadLimitOptions { MaxImageBytes = 4 };

        Action imageLimit = () => AIRequestValidator.ValidateImageEditRequest(request, limits);
        imageLimit.Should().Throw<AIConfigurationException>().WithMessage("*configured limit*");
    }

    [Fact]
    public void ValidateImageEditRequest_ShouldThrow_WhenMaskIsEmpty()
    {
        var request = new AIImageEditRequest
        {
            Prompt = "edit",
            Image = new AIBinaryContent { Content = [1], ContentType = "image/png" },
            Mask = new AIBinaryContent { Content = [], ContentType = "image/png" },
        };

        Action mask = () => AIRequestValidator.ValidateImageEditRequest(request);
        mask.Should().Throw<AIConfigurationException>().WithMessage("*mask*binary content*");
    }
}
