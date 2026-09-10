using System.Net;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIHttpExecutionTests
{
    [Theory]
    [InlineData(AICapability.Chat)]
    [InlineData(AICapability.ImageGeneration)]
    [InlineData(AICapability.Tts)]
    public async Task SharedRetryKeepsCapabilityAndBoundsAttempts(AICapability capability)
    {
        var attempts = 0;
        var expected = new AIRateLimitException("limited", capability, retryAfter: TimeSpan.Zero);
        var exception = await Assert.ThrowsAsync<AIRateLimitException>(() => AIHttpExecution.ExecuteAsync<int>(
            "test", capability, Model with { MaxRetryAttempts = 2 }, "id", TimeProvider.System,
            _ => { attempts++; return Task.FromException<int>(expected); }, default));
        Assert.Same(expected, exception);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("task")]
    [InlineData("http")]
    public async Task TransportFailuresHaveConsistentCategories(string failure)
    {
        Exception original = failure switch
        {
            "operation" => new OperationCanceledException(),
            "task" => new TaskCanceledException(),
            _ => new HttpRequestException("unavailable")
        };
        var exception = await Assert.ThrowsAnyAsync<AIProviderException>(() => AIHttpExecution.ExecuteAsync<int>(
            "test", AICapability.Chat, Model, "id", TimeProvider.System,
            _ => Task.FromException<int>(original), default));
        Assert.Same(original, exception.InnerException);
        if (failure == "http") Assert.Equal(AIErrorCodes.ProviderUnavailable, exception.ErrorCode);
        else Assert.IsType<AITimeoutException>(exception);
    }

    [Fact]
    public async Task CallerCancellationNeverRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AIHttpExecution.ExecuteAsync<int>(
            "test", AICapability.Chat, Model with { MaxRetryAttempts = 3 }, "id", TimeProvider.System,
            token =>
            {
                attempts++;
                cancellation.Cancel();
                return Task.FromCanceled<int>(token);
            }, cancellation.Token));
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AIErrorCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, AIErrorCodes.ProviderAuthenticationFailed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AIErrorCodes.ProviderUnavailable)]
    public async Task ErrorEnvelopesShareTranslationWithoutCopyingResponseBody(HttpStatusCode status, string code)
    {
        using var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"code":"upstream_error","message":"request failed","request_id":"upstream-id","prompt":"private"}""")
        };
        var exception = await AIProviderErrors.CreateAsync("test", response, AICapability.ImageGeneration, Model, "id", default);
        Assert.Equal(code, exception.ErrorCode);
        Assert.Equal("upstream-id", exception.ProviderRequestId);
        Assert.DoesNotContain("private", exception.ToString());
    }

    private static readonly AIResolvedModel Model = new() { Provider = "test", Model = "test" };
}
