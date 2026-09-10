using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Messaging;
using MS.Microservice.Persistence.EFCore.Outbox;
using MS.Microservice.Infrastructure.Telemetry;
using Wolverine;

namespace MS.Microservice.Infrastructure.Messaging;

public sealed class OutboxPublisher(
    IOutboxStore outboxStore,
    IMessageBus messageBus,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider timeProvider,
    PlatformMetrics metrics,
    PlatformTracing tracing,
    ILogger<OutboxPublisher> logger)
{
    private readonly OutboxPublisherOptions _options = options.Value;

    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken = default)
    {
        var lockToken = Guid.NewGuid();
        var messages = await outboxStore.ClaimPendingAsync(
            _options.BatchSize,
            lockToken,
            timeProvider.GetUtcNow(),
            _options.LockDuration,
            cancellationToken);
        metrics.RecordOutboxClaimed(messages.Count);

        foreach (var message in messages)
        {
            var startedAt = Stopwatch.GetTimestamp();
            using var activity = tracing.StartActivity(
                "messaging.outbox.publish",
                ActivityKind.Producer,
                message.TraceParent,
                message.TraceState);
            activity?.SetTag("messaging.system", "wolverine");
            activity?.SetTag("messaging.operation.name", "publish");
            activity?.SetTag("messaging.message.id", message.MessageId.ToString("N"));
            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["MessageId"] = message.MessageId,
                ["CorrelationId"] = message.CorrelationId,
                ["MessagingStage"] = "outbox.publish"
            });
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var messageType = Type.GetType(message.MessageType, throwOnError: true)!;
                var payload = JsonSerializer.Deserialize(message.Payload, messageType)
                    ?? throw new JsonException($"Outbox message '{message.MessageId}' deserialized to null.");
                var stableMessageId = message.MessageId.ToString("N");
                var deliveryOptions = new DeliveryOptions
                {
                    DeduplicationId = stableMessageId
                };
                deliveryOptions.Headers[MessageHeaders.MessageId] = stableMessageId;
                SetHeader(deliveryOptions, MessageHeaders.TraceParent, activity?.Id ?? message.TraceParent);
                SetHeader(deliveryOptions, MessageHeaders.TraceState, activity?.TraceStateString ?? message.TraceState);
                SetHeader(deliveryOptions, MessageHeaders.CorrelationId, message.CorrelationId);
                await messageBus.PublishAsync(payload, deliveryOptions);
                await outboxStore.MarkPublishedAsync(
                    message.MessageId,
                    lockToken,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                metrics.RecordOutboxPublished(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                activity?.SetTag("messaging.outcome", "published");
                logger.LogInformation("Published Outbox message");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var error = SanitizeError(exception.Message);
                var nextRetryAttempt = message.RetryCount + 1;
                var willDeadLetter = nextRetryAttempt > message.MaxRetryCount;
                await outboxStore.MarkFailedAsync(
                    message.MessageId,
                    lockToken,
                    error,
                    timeProvider.GetUtcNow(),
                    CalculateRetryDelay(nextRetryAttempt),
                    cancellationToken);
                metrics.RecordOutboxFailed(
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    willDeadLetter);
                activity?.SetTag("messaging.outcome", willDeadLetter ? "dead_lettered" : "failed");
                activity?.SetStatus(ActivityStatusCode.Error, error);
                logger.LogWarning("Outbox publish failed with outcome {Outcome}", willDeadLetter ? "dead_lettered" : "failed");
            }
        }

        return messages.Count;
    }

    private static void SetHeader(DeliveryOptions options, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) options.Headers[key] = value;
    }

    public TimeSpan CalculateRetryDelay(int retryAttempt)
    {
        if (retryAttempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAttempt));
        }

        var multiplier = Math.Pow(_options.RetryBackoffFactor, retryAttempt - 1);
        var delayTicks = _options.InitialRetryDelay.Ticks * multiplier;
        return TimeSpan.FromTicks((long)Math.Min(delayTicks, _options.MaximumRetryDelay.Ticks));
    }

    private static string SanitizeError(string error)
    {
        var singleLine = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 4000 ? singleLine : singleLine[..4000];
    }
}
