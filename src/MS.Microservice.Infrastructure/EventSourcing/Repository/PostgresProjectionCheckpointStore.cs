using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.EventSourcing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.EventSourcing.Repository
{
    public sealed class PostgresProjectionCheckpointStore : IProjectionCheckpointStore
    {
        private readonly EventStoreDbContext _dbContext;

        public PostgresProjectionCheckpointStore(EventStoreDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<long> GetLastPositionAsync(string projectionName, CancellationToken cancellationToken = default)
        {
            var checkpoint = await _dbContext.ProjectionCheckpoints
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProjectionName == projectionName, cancellationToken);

            return checkpoint?.LastGlobalPosition ?? 0;
        }

        public async Task StoreAsync(string projectionName, long lastGlobalPosition, CancellationToken cancellationToken = default)
        {
            var checkpoint = await _dbContext.ProjectionCheckpoints
                .SingleOrDefaultAsync(x => x.ProjectionName == projectionName, cancellationToken);

            if (checkpoint is null)
            {
                _dbContext.ProjectionCheckpoints.Add(new ProjectionCheckpointRecord
                {
                    ProjectionName = projectionName,
                    LastGlobalPosition = lastGlobalPosition,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                checkpoint.LastGlobalPosition = lastGlobalPosition;
                checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
