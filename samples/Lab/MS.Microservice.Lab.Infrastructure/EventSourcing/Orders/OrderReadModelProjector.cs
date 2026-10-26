using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace MS.Microservice.Infrastructure.EventSourcing.Orders
{
    public sealed class OrderReadModelProjector
    {
        public const string ProjectionName = "order-read-model";
        private const int BatchSize = 100;

        private readonly EventStoreDbContext _dbContext;
        private readonly IEventBatchReader _eventStore;
        private readonly IProjectionCheckpointStore _checkpointStore;

        public OrderReadModelProjector(
            EventStoreDbContext dbContext,
            IEventStore eventStore,
            IProjectionCheckpointStore checkpointStore)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(eventStore);
            ArgumentNullException.ThrowIfNull(checkpointStore);
            _dbContext = dbContext;
            _eventStore = eventStore as IEventBatchReader
                ?? throw new ArgumentException("Projection requires an event store that implements IEventBatchReader.", nameof(eventStore));
            _checkpointStore = checkpointStore;
        }

        public async Task<long> ProjectAsync(CancellationToken cancellationToken = default)
        {
            if (_dbContext.Database.CurrentTransaction is not null || Transaction.Current is not null
                || _dbContext.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Projection requires a context without pending changes or an external transaction.");

            var lastPosition = await _checkpointStore.GetLastPositionAsync(ProjectionName, cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var events = await _eventStore.ReadBatchAsync<OrderEvent>(lastPosition, BatchSize,
                    OrderAggregate.StreamType, cancellationToken);
                if (events.Count == 0) return lastPosition;
                await ProjectBatchAsync(events, cancellationToken);
                lastPosition = events[^1].GlobalPosition;
                if (events.Count < BatchSize) return lastPosition;
            }
        }

        private async Task ProjectBatchAsync(IReadOnlyList<EventEnvelope<OrderEvent>> events, CancellationToken cancellationToken)
        {
            var orderIds = events.Select(envelope => envelope.Data.OrderId.ToString("D")).ToHashSet(StringComparer.Ordinal);
            bool BelongsToBatch(EntityEntry entry) => entry.Entity is OrderReadModel model && orderIds.Contains(model.OrderId)
                || entry.Entity is ProjectionCheckpointRecord checkpoint && checkpoint.ProjectionName == ProjectionName;
            var originals = _dbContext.ChangeTracker.Entries().Where(BelongsToBatch)
                .Select(entry => new TrackedSnapshot(entry, entry.CurrentValues.Clone(), entry.OriginalValues.Clone())).ToArray();
            var originalEntities = originals.Select(snapshot => snapshot.Entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            var originalModels = originals.Where(snapshot => snapshot.Entry.Entity is OrderReadModel)
                .ToDictionary(snapshot => ((OrderReadModel)snapshot.Entry.Entity).OrderId, snapshot => snapshot.Entry, StringComparer.Ordinal);
            var committed = false;
            try
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                // Read database values even when the caller has an unchanged tracked model. A previous
                // commit may have succeeded despite a lost acknowledgement, leaving an older memory snapshot.
                var models = await _dbContext.OrderReadModels.AsNoTracking().Where(model => orderIds.Contains(model.OrderId))
                    .ToDictionaryAsync(model => model.OrderId, StringComparer.Ordinal, cancellationToken);
                foreach (var model in models.Values.ToArray())
                {
                    if (originalModels.TryGetValue(model.OrderId, out var entry))
                    {
                        entry.CurrentValues.SetValues(model);
                        entry.OriginalValues.SetValues(model);
                        entry.State = EntityState.Unchanged;
                        models[model.OrderId] = (OrderReadModel)entry.Entity;
                    }
                    else _dbContext.OrderReadModels.Attach(model);
                }

                foreach (var envelope in events)
                {
                    var orderId = envelope.Data.OrderId.ToString("D");
                    if (!models.TryGetValue(orderId, out var model))
                    {
                        if (originalModels.TryGetValue(orderId, out var entry))
                        {
                            // The caller may still track a row removed from the database. Reuse its
                            // identity with fresh initial values instead of attaching a duplicate instance.
                            entry.CurrentValues.SetValues(new OrderReadModel { OrderId = orderId });
                            entry.State = EntityState.Added;
                            model = (OrderReadModel)entry.Entity;
                        }
                        else
                        {
                            model = new OrderReadModel { OrderId = orderId };
                            _dbContext.OrderReadModels.Add(model);
                        }
                        models.Add(orderId, model);
                    }
                    OrderReadModelProjection.Apply(model, envelope);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await _checkpointStore.StoreAsync(ProjectionName, events[^1].GlobalPosition, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            finally
            {
                // The transaction is disposed (and rolled back when necessary) before tracker cleanup.
                if (!committed)
                    foreach (var snapshot in originals)
                    {
                        snapshot.Entry.CurrentValues.SetValues(snapshot.Current);
                        snapshot.Entry.OriginalValues.SetValues(snapshot.Original);
                        snapshot.Entry.State = EntityState.Unchanged;
                    }
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(BelongsToBatch).ToArray())
                    if (!originalEntities.Contains(entry.Entity)) entry.State = EntityState.Detached;
            }
        }

        private sealed record TrackedSnapshot(EntityEntry Entry, PropertyValues Current, PropertyValues Original);
    }
}
