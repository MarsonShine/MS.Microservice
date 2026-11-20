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
    public sealed class PostgresEventStore : IEventStore, IEventBatchReader
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
            var records = _dbContext.EventStore
                .AsNoTracking()
                .Where(x => x.StreamId == streamId && x.Version >= fromVersion)
                .OrderBy(x => x.Version);
            return Map<TEvent>(await records.ToListAsync(cancellationToken));
        }

        public async Task<IReadOnlyList<EventEnvelope<TEvent>>> ReadAllAsync<TEvent>(
            long afterGlobalPosition,
            string? streamType = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent
        {
            var records = EventsAfter(afterGlobalPosition, streamType).OrderBy(x => x.GlobalPosition);
            return Map<TEvent>(await records.ToListAsync(cancellationToken));
        }

        public async Task<IReadOnlyList<EventEnvelope<TEvent>>> ReadBatchAsync<TEvent>(
            long afterGlobalPosition,
            int take = 100,
            string? streamType = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent
        {
            if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
            var records = EventsAfter(afterGlobalPosition, streamType).OrderBy(x => x.GlobalPosition).Take(take);
            return Map<TEvent>(await records.ToListAsync(cancellationToken));
        }

        private IQueryable<EventStoreRecord> EventsAfter(long afterGlobalPosition, string? streamType)
        {
            var query = _dbContext.EventStore.AsNoTracking().Where(x => x.GlobalPosition > afterGlobalPosition);
            return string.IsNullOrWhiteSpace(streamType) ? query : query.Where(x => x.StreamType == streamType);
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

        private EventEnvelope<TEvent>[] Map<TEvent>(List<EventStoreRecord> records)
            where TEvent : class, IEventSourcedEvent
        {
            if (records.Count == 0) return [];
            var result = new EventEnvelope<TEvent>[records.Count];
            string? previousJson = null;
            EventMetadata? metadata = null;
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                var payload = _serializer.Deserialize<TEvent>(record.EventType, record.Payload);
                if (!_serializer.UsesDefaultOptions || metadata is null || record.Metadata != previousJson)
                {
                    metadata = _serializer.DeserializeMetadata(record.Metadata);
                    previousJson = record.Metadata;
                }
                result[index] = new(record.EventId, record.StreamId, record.StreamType, record.Version,
                    record.GlobalPosition, payload, metadata, record.CreatedAt);
            }
            return result;
        }
    }
}
