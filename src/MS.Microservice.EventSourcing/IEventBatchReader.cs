namespace MS.Microservice.Domain.EventSourcing;

/// <summary>Optional bounded reads ordered by global position, independent of the legacy full-history API.</summary>
public interface IEventBatchReader
{
    /// <summary>Reads up to 1–1000 matching events strictly after the supplied position; use the last returned position for the next page.</summary>
    Task<IReadOnlyList<EventEnvelope<TEvent>>> ReadBatchAsync<TEvent>(
        long afterGlobalPosition,
        int take = 100,
        string? streamType = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEventSourcedEvent;
}
