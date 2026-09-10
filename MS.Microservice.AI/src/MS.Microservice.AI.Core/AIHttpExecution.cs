using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

/// <summary>统一 Chat/Media 的每次尝试超时、瞬时失败重试和取消分类，不承担各能力的响应解析。</summary>
internal static class AIHttpExecution
{
    public static async Task<T> ExecuteAsync<T>(string provider, AICapability capability, AIResolvedModel model,
        string? requestId, TimeProvider clock, Func<CancellationToken, Task<T>> send, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = new CancellationTokenSource(model.Timeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            Exception failure;
            TimeSpan? retryAfter = null;
            try { return await send(linked.Token).ConfigureAwait(false); }
            // 调用方主动结束请求不代表服务不可用，必须先于超时分类处理，且不能重试。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException exception)
            {
                failure = new AITimeoutException($"AI provider '{provider}' timed out.", capability,
                    provider: provider, model: model.Model, scenario: model.Scenario, requestId: requestId, innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                failure = new AIProviderException($"AI provider '{provider}' is unavailable.", AIErrorCodes.ProviderUnavailable,
                    capability, provider: provider, model: model.Model, scenario: model.Scenario, requestId: requestId,
                    isTransient: true, innerException: exception);
            }
            catch (AIProviderException exception) when (exception.IsTransient)
            {
                failure = exception;
                retryAfter = exception.RetryAfter;
            }
            if (attempt >= model.MaxRetryAttempts)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt), 2000));
            if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
