using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.EventSourcing.Repository
{
    public sealed class PostgresSnapshotStore : ISnapshotStore
    {
        private readonly EventStoreDbContext _dbContext;
        private readonly SystemTextJsonEventSerializer _serializer;

        public PostgresSnapshotStore(EventStoreDbContext dbContext, SystemTextJsonEventSerializer serializer)
        {
            _dbContext = dbContext;
            _serializer = serializer;
        }

        public async Task<AggregateSnapshot<TState>?> GetLatestAsync<TState>(string streamId, CancellationToken cancellationToken = default)
        {
            var snapshot = await _dbContext.Snapshots
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StreamId == streamId, cancellationToken);

            if (snapshot is null)
            {
                return null;
            }

            return new AggregateSnapshot<TState>(
                snapshot.StreamId,
                snapshot.StreamType,
                snapshot.Version,
                _serializer.DeserializeState<TState>(snapshot.State),
                snapshot.CreatedAt);
        }

        public async Task UpsertAsync<TState>(AggregateSnapshot<TState> snapshot, CancellationToken cancellationToken = default)
        {
            var existing = await _dbContext.Snapshots
                .SingleOrDefaultAsync(x => x.StreamId == snapshot.StreamId, cancellationToken);

            var state = _serializer.SerializeState(snapshot.State);
            if (existing is null)
            {
                _dbContext.Snapshots.Add(new SnapshotRecord
                {
                    StreamId = snapshot.StreamId,
                    StreamType = snapshot.StreamType,
                    Version = snapshot.Version,
                    State = state,
                    CreatedAt = snapshot.CreatedAt,
                });
            }
            else
            {
                existing.StreamType = snapshot.StreamType;
                existing.Version = snapshot.Version;
                existing.State = state;
                existing.CreatedAt = snapshot.CreatedAt;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
