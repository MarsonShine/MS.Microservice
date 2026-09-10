namespace MS.Microservice.Messaging;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredAtUtc { get; }
}

public interface IIntegrationEventPublisher
{
    /// <summary>Stages an immutable message in the active unit of work, without sending it to a broker.</summary>
    ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    /// <summary>Executes and commits one business operation. Nested calls participate in the outer transaction.</summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

public static class UnitOfWorkExtensions
{
    public static async Task ExecuteAsync(this IUnitOfWork unitOfWork,
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await unitOfWork.ExecuteAsync(async token => { await operation(token); return true; }, cancellationToken);
    }
}

public sealed record MessageContext(Guid MessageId, string Consumer, string? CorrelationId = null,
    string? TraceParent = null, string? TraceState = null);

public interface IIntegrationEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken);
}

public enum ReplayResult { Accepted, NotFound, InvalidState }

public sealed record FailedMessage(string FailureId, Guid MessageId, string ContractName, int ContractVersion,
    string? Consumer, DateTimeOffset? FailedAtUtc, string ErrorCode);

public interface IFailedMessageOperations
{
    Task<IReadOnlyList<FailedMessage>> ListAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<ReplayResult> ReplayAsync(string failureId, CancellationToken cancellationToken = default);
}

public enum MessagingProvider { SelfManaged, Wolverine }

public sealed class MessageContractException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>A business handler can reject a message permanently; infrastructure failures remain retryable.</summary>
public sealed class PermanentMessageException(string errorCode) : Exception(errorCode);
