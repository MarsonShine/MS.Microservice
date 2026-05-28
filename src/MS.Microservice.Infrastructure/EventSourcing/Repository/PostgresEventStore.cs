using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Serialization;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.EventSourcing.Repository
{
    public sealed class PostgresEventStore : IEventStore
    {
        private readonly EventStoreDbContext _dbContext;
        private readonly SystemTextJsonEventSerializer _serializer;

        public PostgresEventStore(EventStoreDbContext dbContext, SystemTextJsonEventSerializer serializer)
        {
            _dbContext = dbContext;
            _serializer = serializer;
        }

        public async Task<IReadOnlyList<EventEnvelope<TEvent>>> LoadStreamAsync<TEvent>(
            string streamId,
            int fromVersion = 1,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent
        {
            var events = await _dbContext.EventStore
                .AsNoTracking()
                .Where(x => x.StreamId == streamId && x.Version >= fromVersion)
                .OrderBy(x => x.Version)
                .ToListAsync(cancellationToken);

            return events.Select(Map<TEvent>).ToArray();
        }

        public async Task<IReadOnlyList<EventEnvelope<TEvent>>> ReadAllAsync<TEvent>(
            long afterGlobalPosition,
            string? streamType = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent
        {
            var query = _dbContext.EventStore
                .AsNoTracking()
                .Where(x => x.GlobalPosition > afterGlobalPosition);

            if (!string.IsNullOrWhiteSpace(streamType))
            {
                query = query.Where(x => x.StreamType == streamType);
            }

            var events = await query
                .OrderBy(x => x.GlobalPosition)
                .ToListAsync(cancellationToken);

            return events.Select(Map<TEvent>).ToArray();
        }

        public async Task AppendToStreamAsync<TEvent>(
            string streamId,
            string streamType,
            int expectedVersion,
            IReadOnlyCollection<TEvent> events,
            Func<TEvent, EventMetadata>? metadataFactory = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            ArgumentException.ThrowIfNullOrWhiteSpace(streamType);
            ArgumentNullException.ThrowIfNull(events);

            if (events.Count == 0)
            {
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var currentVersion = await _dbContext.EventStore
                .Where(x => x.StreamId == streamId)
                .Select(x => (int?)x.Version)
                .MaxAsync(cancellationToken) ?? 0;

            if (currentVersion != expectedVersion)
            {
                throw new EventStoreConcurrencyException(streamId, expectedVersion, currentVersion);
            }

            var version = currentVersion;
            var eventRecords = events.Select(@event =>
            {
                version++;
                var serialized = _serializer.Serialize(@event, metadataFactory?.Invoke(@event) ?? new EventMetadata());
                return new EventStoreRecord
                {
                    EventId = Guid.NewGuid(),
                    StreamId = streamId,
                    StreamType = streamType,
                    Version = version,
                    EventType = serialized.EventType,
                    Payload = serialized.Payload,
                    Metadata = serialized.Metadata,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
            }).ToList();

            _dbContext.EventStore.AddRange(eventRecords);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new EventStoreConcurrencyException(streamId, expectedVersion, currentVersion, exception);
            }
        }

        private EventEnvelope<TEvent> Map<TEvent>(EventStoreRecord record)
            where TEvent : class, IEventSourcedEvent
        {
            var @event = _serializer.Deserialize<TEvent>(record.EventType, record.Payload);
            var metadata = _serializer.DeserializeMetadata(record.Metadata);
            return new EventEnvelope<TEvent>(
                record.EventId,
                record.StreamId,
                record.StreamType,
                record.Version,
                record.GlobalPosition,
                @event,
                metadata,
                record.CreatedAt);
        }
    }
}
