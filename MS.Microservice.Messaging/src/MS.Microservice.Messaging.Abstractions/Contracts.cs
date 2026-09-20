namespace MS.Microservice.Messaging;

/// <summary>跨边界传播的业务事实；Id 和 UTC 发生时间在首次产生时确定，重试和重放保持不变。</summary>
public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>向当前工作单元暂存事件，不直接承诺网络发送成功。</summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Stages an immutable message in the active unit of work, without sending it to a broker.</summary>
    ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken cancellationToken = default);
}

/// <summary>业务变更与出站消息的原子提交入口，由最外层调用拥有事务。</summary>
/// <remarks>Handler 不应自行提交，否则消费完成标记和后续消息可能落在不同事务中。</remarks>
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

/// <summary>业务消费契约；适配器提供上下文与事务，业务不依赖原生信封或消息表。</summary>
public interface IIntegrationEventHandler;

public interface IIntegrationEventHandler<in T> : IIntegrationEventHandler where T : IIntegrationEvent
{
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken);
}

public enum ReplayResult { Accepted, NotFound, InvalidState }

public sealed record FailedMessage(string FailureId, Guid MessageId, string ContractName, int ContractVersion,
    string? Consumer, DateTimeOffset? FailedAtUtc, string ErrorCode);

/// <summary>失败消息的最小共同运维能力；标识由 Provider 管理，不能按某一实现的表结构解析。</summary>
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
