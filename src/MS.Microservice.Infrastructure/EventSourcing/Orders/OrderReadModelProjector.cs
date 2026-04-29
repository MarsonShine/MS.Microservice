using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.EventSourcing.Orders
{
    public sealed class OrderReadModelProjector
    {
        public const string ProjectionName = "order-read-model";

        private readonly EventStoreDbContext _dbContext;
        private readonly IEventStore _eventStore;
        private readonly IProjectionCheckpointStore _checkpointStore;

        public OrderReadModelProjector(
            EventStoreDbContext dbContext,
            IEventStore eventStore,
            IProjectionCheckpointStore checkpointStore)
        {
            _dbContext = dbContext;
            _eventStore = eventStore;
            _checkpointStore = checkpointStore;
        }

        public async Task<long> ProjectAsync(CancellationToken cancellationToken = default)
        {
            var lastPosition = await _checkpointStore.GetLastPositionAsync(ProjectionName, cancellationToken);
            var events = await _eventStore.ReadAllAsync<OrderEvent>(lastPosition, OrderAggregate.StreamType, cancellationToken);
            if (events.Count == 0)
            {
                return lastPosition;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            foreach (var envelope in events)
            {
                var orderId = envelope.Data.OrderId.ToString("D");
                var readModel = await _dbContext.OrderReadModels
                    .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

                var projected = OrderReadModelProjection.Apply(readModel, envelope);
                if (readModel is null)
                {
                    _dbContext.OrderReadModels.Add(projected);
                }

                lastPosition = envelope.GlobalPosition;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _checkpointStore.StoreAsync(ProjectionName, lastPosition, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return lastPosition;
        }
    }
}
