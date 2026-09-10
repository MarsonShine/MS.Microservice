using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

/// <summary>One scoped context owns business data and messages. The instance must not be used concurrently.</summary>
public sealed class SelfManagedUnitOfWork<TContext>(TContext context, MessageContractRegistry contracts,
    TimeProvider timeProvider) : IUnitOfWork, IIntegrationEventPublisher where TContext : DbContext
{
    private bool _active;
    private bool _rollbackOnly;
    private readonly Dictionary<Guid, SerializedMessage> _pending = [];

    public ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_active) throw new InvalidOperationException("Enqueue requires an active unit of work.");
        var activity = Activity.Current;
        var serialized = contracts.Serialize(message, new(message.Id, "",
            activity?.GetBaggageItem("correlationId"), activity?.Id, activity?.TraceStateString));
        if (_pending.TryGetValue(message.Id, out var existing) && existing != serialized)
            throw new MessageContractException("A message Id cannot identify different payloads in one transaction.");
        _pending[message.Id] = serialized;
        return ValueTask.CompletedTask;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (_active)
        {
            try { return await operation(cancellationToken); }
            catch { _rollbackOnly = true; throw; }
        }
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The messaging unit of work must own the outer transaction.");
        if (context.Model.FindEntityType(typeof(OutboxEntry)) is null)
            throw new InvalidOperationException("Call AddSelfManagedMessaging in the business context model.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        _active = true;
        _rollbackOnly = false;
        try
        {
            var result = await operation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_rollbackOnly) throw new InvalidOperationException("A nested operation failed; this transaction cannot commit.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var message in _pending.Values) context.Set<OutboxEntry>().Add(OutboxEntry.From(message, now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // Disposing the transaction rolls it back even when caller cancellation is already requested.
            context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _pending.Clear();
            _active = false;
        }
    }
}
