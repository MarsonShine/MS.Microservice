using System.Text.Json;
using Microsoft.Extensions.Options;
using MS.Microservice.Persistence.EFCore.Outbox;
using Wolverine;

namespace MS.Microservice.Infrastructure.Messaging;

public sealed class OutboxPublisher(
    IOutboxStore outboxStore,
    IMessageBus messageBus,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider timeProvider)
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

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var messageType = Type.GetType(message.MessageType, throwOnError: true)!;
                var payload = JsonSerializer.Deserialize(message.Payload, messageType)
                    ?? throw new JsonException($"Outbox message '{message.MessageId}' deserialized to null.");
                await messageBus.PublishAsync(payload);
                await outboxStore.MarkPublishedAsync(
                    message.MessageId,
                    lockToken,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var error = SanitizeError(exception.Message);
                await outboxStore.MarkFailedAsync(
                    message.MessageId,
                    lockToken,
                    error,
                    timeProvider.GetUtcNow(),
                    _options.FailureRetryDelay,
                    cancellationToken);
            }
        }

        return messages.Count;
    }

    private static string SanitizeError(string error)
    {
        var singleLine = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 4000 ? singleLine : singleLine[..4000];
    }
}
