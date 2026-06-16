using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

/// <summary>
/// 默认 AI 日志脱敏器。
/// 
/// 主要职责：
/// 1. 对 Authorization / Bearer Token 进行脱敏；
/// 2. 对配置中声明的敏感字段进行脱敏；
/// 3. 避免 API Key、Token、Prompt 中的敏感内容直接进入日志。
/// </summary>
internal sealed partial class DefaultAILogSanitizer(IOptionsMonitor<AILogSanitizerOptions> options) : IAILogSanitizer
{
    private readonly IOptionsMonitor<AILogSanitizerOptions> _options = options;

    public string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redaction = _options.CurrentValue.RedactionText;
        var sanitized = BearerRegex().Replace(value, $"Bearer {redaction}");
        sanitized = AuthorizationRegex().Replace(sanitized, $"Authorization: {redaction}");

        foreach (var field in _options.CurrentValue.SensitiveFields)
        {
            sanitized = Regex.Replace(
                sanitized,
                $"(?i)(\\\"?{Regex.Escape(field)}\\\"?\\s*[:=]\\s*)\\\"?[^,;\\r\\n}}\\\"]+\\\"?",
                $"$1{redaction}");
        }

        return sanitized;
    }

    public IReadOnlyDictionary<string, string?> SanitizeMetadata(IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var sensitiveFields = _options.CurrentValue.SensitiveFields;
        var redaction = _options.CurrentValue.RedactionText;
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata)
        {
            result[pair.Key] = sensitiveFields.Contains(pair.Key) ? redaction : Sanitize(pair.Value);
        }

        return result;
    }

    [GeneratedRegex("Bearer\\s+[-._~+/A-Za-z0-9]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex("Authorization\\s*[:=]\\s*[^,;\\r\\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();
}

internal sealed class DefaultAIRateLimiter(IOptionsMonitor<AIRateLimitingOptions> options, TimeProvider timeProvider) : IAIRateLimiter
{
    private readonly IOptionsMonitor<AIRateLimitingOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ConcurrentDictionary<string, FixedWindowCounter> _counters = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<AIRateLimitLease> AcquireAsync(AIRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.CurrentValue;
        if (!options.Enabled || options.RequestsPerWindow is null)
        {
            return ValueTask.FromResult(AIRateLimitLease.Noop);
        }

        var key = $"{context.Provider}:{context.Model}";
        var counter = _counters.GetOrAdd(key, _ => new FixedWindowCounter());
        var now = _timeProvider.GetUtcNow();
        var window = TimeSpan.FromSeconds(options.WindowSeconds);

        lock (counter)
        {
            if (now >= counter.WindowStartedAt.Add(window))
            {
                counter.WindowStartedAt = now;
                counter.Count = 0;
            }

            if (counter.Count >= options.RequestsPerWindow.Value)
            {
                throw new AIRateLimitException(
                    $"AI local rate limit exceeded for provider '{context.Provider}' and model '{context.Model}'.",
                    context.Capability,
                    provider: context.Provider,
                    model: context.Model,
                    scenario: context.Scenario,
                    requestId: context.RequestId,
                    retryAfter: counter.WindowStartedAt.Add(window) - now);
            }

            counter.Count++;
        }

        return ValueTask.FromResult(AIRateLimitLease.Noop);
    }

    private sealed class FixedWindowCounter
    {
        public DateTimeOffset WindowStartedAt { get; set; } = DateTimeOffset.MinValue;
        public int Count { get; set; }
    }
}

internal sealed class DefaultAICircuitBreaker(IOptionsMonitor<AICircuitBreakerOptions> options, TimeProvider timeProvider) : IAICircuitBreaker
{
    private readonly IOptionsMonitor<AICircuitBreakerOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask EnsureAllowedAsync(AIRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return ValueTask.CompletedTask;
        }

        var key = $"{context.Provider}:{context.Model}";
        if (_states.TryGetValue(key, out var state) && state.OpenedUntilUtc > _timeProvider.GetUtcNow())
        {
            throw new AIProviderException(
                $"AI circuit breaker is open for provider '{context.Provider}' and model '{context.Model}'.",
                AIErrorCodes.CircuitOpen,
                context.Capability,
                provider: context.Provider,
                model: context.Model,
                scenario: context.Scenario,
                requestId: context.RequestId,
                isTransient: true,
                retryAfter: state.OpenedUntilUtc - _timeProvider.GetUtcNow());
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordSuccessAsync(AIRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _states.TryRemove($"{context.Provider}:{context.Model}", out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask RecordFailureAsync(AIRequestContext context, Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return ValueTask.CompletedTask;
        }

        var key = $"{context.Provider}:{context.Model}";
        var state = _states.AddOrUpdate(
            key,
            _ => new CircuitState { FailureCount = 1 },
            (_, existing) => existing with { FailureCount = existing.FailureCount + 1 });

        if (state.FailureCount >= options.FailureThreshold)
        {
            _states[key] = state with { OpenedUntilUtc = _timeProvider.GetUtcNow().AddSeconds(options.BreakDurationSeconds) };
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 单个 Provider + Model 的熔断状态。
    /// </summary>
    private sealed record CircuitState
    {
        /// <summary>
        /// 当前累计失败次数。
        /// </summary>
        public int FailureCount { get; init; }

        /// <summary>
        /// 熔断打开到什么时候。
        /// 如果当前时间小于该值，则请求会被拒绝。
        /// </summary>
        public DateTimeOffset OpenedUntilUtc { get; init; }
    }
}

internal sealed class EnvironmentAISecretProvider(IOptionsMonitor<AISecretProviderOptions> options) : IAISecretProvider
{
    private readonly IOptionsMonitor<AISecretProviderOptions> _options = options;

    public string? GetSecret(AISecretRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrWhiteSpace(request.SecretName))
        {
            var explicitValue = Environment.GetEnvironmentVariable(request.SecretName);
            if (!string.IsNullOrWhiteSpace(explicitValue))
            {
                return explicitValue;
            }
        }

        var derivedName = $"{_options.CurrentValue.EnvironmentVariablePrefix}{request.Provider}__ApiKey";
        return Environment.GetEnvironmentVariable(derivedName);
    }
}

internal sealed class ConfigurationAISecretProvider(IConfiguration configuration) : IAISecretProvider
{
    private readonly IConfiguration _configuration = configuration;

    public string? GetSecret(AISecretRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.IsNullOrWhiteSpace(request.SecretName) ? null : _configuration[request.SecretName];
    }
}

internal sealed class CompositeAISecretProvider(
    EnvironmentAISecretProvider environmentProvider, 
    ConfigurationAISecretProvider configurationProvider, 
    IOptionsMonitor<AISecretProviderOptions> options) : IAISecretProvider
{
    private readonly EnvironmentAISecretProvider _environmentProvider = environmentProvider;
    private readonly ConfigurationAISecretProvider _configurationProvider = configurationProvider;
    private readonly IOptionsMonitor<AISecretProviderOptions> _options = options;

    public string? GetSecret(AISecretRequest request)
    {
        return _options.CurrentValue.PreferEnvironment
            ? _environmentProvider.GetSecret(request) ?? _configurationProvider.GetSecret(request)
            : _configurationProvider.GetSecret(request) ?? _environmentProvider.GetSecret(request);
    }
}

/// <summary>
/// AIOptions 后置配置器。
/// 
/// 主要职责：
/// 在 AIOptions 被绑定完成后，如果 Provider 没有直接配置 ApiKey，
/// 则尝试通过 IAISecretProvider 根据 ApiKeySecretName 或约定环境变量名补齐 ApiKey。
/// 
/// 这样可以避免把密钥硬编码在 appsettings.json 中。
/// </summary>
internal sealed class AIOptionsSecretPostConfigure(IAISecretProvider secretProvider) : IPostConfigureOptions<AIOptions>
{
    private readonly IAISecretProvider _secretProvider = secretProvider;

    /// <summary>
    /// Microsoft.Extensions.Options 会在 AIOptions 创建后调用该方法。
    /// </summary>
    /// <param name="name"></param>
    /// <param name="options"></param>
    public void PostConfigure(string? name, AIOptions options)
    {
        foreach (var provider in options.Providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.Value.ApiKey))
            {
                continue;
            }

            provider.Value.ApiKey = _secretProvider.GetSecret(new AISecretRequest
            {
                Provider = provider.Key,
                SecretName = provider.Value.ApiKeySecretName,
            });
        }
    }
}

internal sealed class NullAICostReporter : IAICostReporter
{
    public ValueTask ReportAsync(AICostRecord record, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
