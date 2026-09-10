using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Infrastructure.EventSourcing.Orders
{
    public sealed class OrderCommandService
    {
        private readonly IEventStore _eventStore;
        private readonly ISnapshotStore _snapshotStore;
        private readonly int _snapshotFrequency;

        public OrderCommandService(IEventStore eventStore, ISnapshotStore snapshotStore, int snapshotFrequency = 100)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotFrequency);
            _eventStore = eventStore;
            _snapshotStore = snapshotStore;
            _snapshotFrequency = snapshotFrequency;
        }

        public async Task<Either<Error, IReadOnlyList<OrderEvent>>> HandleAsync(
            OrderCommand command,
            EventMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var streamId = OrderAggregate.GetStreamId(command.OrderId);
            var snapshot = await _snapshotStore.GetLatestAsync<OrderState>(streamId, cancellationToken);
            var history = await _eventStore.LoadStreamAsync<OrderEvent>(
                streamId,
                (snapshot?.Version ?? 0) + 1,
                cancellationToken);

            var state = OrderAggregate.Fold(history.Select(x => x.Data), snapshot?.State);
            var decision = OrderAggregate.Decide(state, command);
            if (decision.IsLeft)
            {
                return Left(decision.Left);
            }

            await _eventStore.AppendToStreamAsync(
                streamId,
                OrderAggregate.StreamType,
                state.Version,
                decision.Right,
                _ => metadata ?? new EventMetadata(),
                cancellationToken);

            var nextState = OrderAggregate.Fold(decision.Right, state);
            if (ShouldCreateSnapshot(nextState.Version))
            {
                await _snapshotStore.UpsertAsync(
                    new AggregateSnapshot<OrderState>(
                        streamId,
                        OrderAggregate.StreamType,
                        nextState.Version,
                        nextState,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            return Right<IReadOnlyList<OrderEvent>>(decision.Right);
        }

        private bool ShouldCreateSnapshot(int version)
            => version > 0 && version % _snapshotFrequency == 0;
    }
}
