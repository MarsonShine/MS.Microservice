using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.EventSourcing
{
    public interface IEventSourcedEvent
    {
    }

    public sealed record EventMetadata(
        string? CorrelationId = null,
        string? CausationId = null,
        string? UserId = null,
        string? TenantId = null,
        string? TraceId = null,
        int SchemaVersion = 1);

    public sealed record EventEnvelope<TEvent>(
        Guid EventId,
        string StreamId,
        string StreamType,
        int Version,
        long GlobalPosition,
        TEvent Data,
        EventMetadata Metadata,
        DateTimeOffset CreatedAt)
        where TEvent : class, IEventSourcedEvent;

    public sealed record AggregateSnapshot<TState>(
        string StreamId,
        string StreamType,
        int Version,
        TState State,
        DateTimeOffset CreatedAt);

    public interface IEventStore
    {
        Task<IReadOnlyList<EventEnvelope<TEvent>>> LoadStreamAsync<TEvent>(
            string streamId,
            int fromVersion = 1,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent;

        Task<IReadOnlyList<EventEnvelope<TEvent>>> ReadAllAsync<TEvent>(
            long afterGlobalPosition,
            string? streamType = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent;

        Task AppendToStreamAsync<TEvent>(
            string streamId,
            string streamType,
            int expectedVersion,
            IReadOnlyCollection<TEvent> events,
            Func<TEvent, EventMetadata>? metadataFactory = null,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEventSourcedEvent;
    }

    public interface ISnapshotStore
    {
        Task<AggregateSnapshot<TState>?> GetLatestAsync<TState>(string streamId, CancellationToken cancellationToken = default);

        Task UpsertAsync<TState>(AggregateSnapshot<TState> snapshot, CancellationToken cancellationToken = default);
    }

    public interface IProjectionCheckpointStore
    {
        Task<long> GetLastPositionAsync(string projectionName, CancellationToken cancellationToken = default);

        Task StoreAsync(string projectionName, long lastGlobalPosition, CancellationToken cancellationToken = default);
    }

    public sealed class EventStoreConcurrencyException : Exception
    {
        public EventStoreConcurrencyException(string streamId, int expectedVersion, int actualVersion)
            : base($"事件流 {streamId} 发生并发冲突，期望版本 {expectedVersion}，实际版本 {actualVersion}。")
        {
            StreamId = streamId;
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }

        public EventStoreConcurrencyException(string streamId, int expectedVersion, int actualVersion, Exception innerException)
            : base($"事件流 {streamId} 发生并发冲突，期望版本 {expectedVersion}，实际版本 {actualVersion}。", innerException)
        {
            StreamId = streamId;
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }

        public string StreamId { get; }

        public int ExpectedVersion { get; }

        public int ActualVersion { get; }
    }
}
