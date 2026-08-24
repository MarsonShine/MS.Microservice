using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;
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
    PlatformMetrics metrics)
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
                await messageBus.PublishAsync(payload, deliveryOptions);
                await outboxStore.MarkPublishedAsync(
                    message.MessageId,
                    lockToken,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                metrics.RecordOutboxPublished(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
            }
        }

        return messages.Count;
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
