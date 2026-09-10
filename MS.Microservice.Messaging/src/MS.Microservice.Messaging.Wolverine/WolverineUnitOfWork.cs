using Microsoft.EntityFrameworkCore;
using global::Wolverine;
using global::Wolverine.EntityFrameworkCore;
using NativeContext = global::Wolverine.Runtime.MessageContext;

namespace MS.Microservice.Messaging.Wolverine;

/// <summary>Adapts the common work boundary to a fresh native outbox for each outer operation.</summary>
public sealed class WolverineUnitOfWork<TContext>(TContext context,
    Func<IDbContextOutbox<TContext>> createOutbox, MessageContractRegistry registry)
    : IUnitOfWork, IIntegrationEventPublisher where TContext : DbContext
{
    private readonly Dictionary<Guid, SerializedMessage> _pending = [];
    private bool _active;
    private bool _rollbackOnly;
    private IMessageContext? _incoming;

    public ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_active && _incoming is null) throw new InvalidOperationException("Enqueue requires an active unit of work.");
        var snapshot = registry.Serialize(message);
        if (_pending.TryGetValue(message.Id, out var existing) && existing != snapshot)
            throw new MessageContractException("A message Id cannot identify different payloads in one transaction.");
        _pending[message.Id] = snapshot;
        return ValueTask.CompletedTask;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (_active || _incoming is not null)
        {
            try { return await operation(cancellationToken); }
            catch { _rollbackOnly = true; throw; }
        }
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The messaging unit of work must own the outer transaction.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var outbox = createOutbox();
        _active = true;
        _rollbackOnly = false;
        try
        {
            var result = await operation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_rollbackOnly) throw new InvalidOperationException("A nested operation failed; this transaction cannot commit.");
            foreach (var snapshot in _pending.Values)
                await outbox.PublishAsync(registry.Deserialize(snapshot));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (outbox is NativeContext native) await native.ClearAllAsync();
            context.ChangeTracker.Clear();
            throw;
        }
        finally { _pending.Clear(); _active = false; }
    }

    internal void BeginIncoming(IMessageContext incoming)
    {
        if (_active || _incoming is not null || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The native Wolverine transaction middleware must own incoming work.");
        _incoming = incoming;
        _rollbackOnly = false;
    }

    internal async Task FlushIncomingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_incoming is null || _rollbackOnly) throw new InvalidOperationException("Incoming transaction cannot commit.");
        foreach (var snapshot in _pending.Values) await _incoming.PublishAsync(registry.Deserialize(snapshot));
    }

    internal void EndIncoming() { _incoming = null; _pending.Clear(); _rollbackOnly = false; }
}
