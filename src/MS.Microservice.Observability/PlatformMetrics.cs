using System.Diagnostics.Metrics;

namespace MS.Microservice.Infrastructure.Telemetry;

public sealed class PlatformMetrics : IDisposable
{
    public const string MeterName = "MS.Microservice";

    private static readonly KeyValuePair<string, object?>[] PublishedOutcome = [new("outcome", "published")];
    private static readonly KeyValuePair<string, object?>[] FailedOutcome = [new("outcome", "failed")];
    private static readonly KeyValuePair<string, object?>[] DeadLetteredOutcome = [new("outcome", "dead_lettered")];
    private static readonly KeyValuePair<string, object?>[] ProcessedOutcome = [new("outcome", "processed")];

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _outboxClaimed;
    private readonly Counter<long> _outboxPublished;
    private readonly Counter<long> _outboxFailed;
    private readonly Counter<long> _outboxDeadLettered;
    private readonly Histogram<double> _outboxPublishDuration;
    private readonly Counter<long> _inboxRegistered;
    private readonly Counter<long> _inboxDuplicate;
    private readonly Counter<long> _inboxShortCircuited;
    private readonly Counter<long> _inboxProcessed;
    private readonly Counter<long> _inboxFailed;
    private readonly Histogram<double> _inboxHandlerDuration;

    public PlatformMetrics()
    {
        _outboxClaimed = _meter.CreateCounter<long>("ms.messaging.outbox.claimed", "{message}");
        _outboxPublished = _meter.CreateCounter<long>("ms.messaging.outbox.published", "{message}");
        _outboxFailed = _meter.CreateCounter<long>("ms.messaging.outbox.failed", "{message}");
        _outboxDeadLettered = _meter.CreateCounter<long>("ms.messaging.outbox.dead_lettered", "{message}");
        _outboxPublishDuration = _meter.CreateHistogram<double>("ms.messaging.outbox.publish.duration", "ms");
        _inboxRegistered = _meter.CreateCounter<long>("ms.messaging.inbox.registered", "{message}");
        _inboxDuplicate = _meter.CreateCounter<long>("ms.messaging.inbox.duplicate", "{message}");
        _inboxShortCircuited = _meter.CreateCounter<long>("ms.messaging.inbox.short_circuited", "{message}");
        _inboxProcessed = _meter.CreateCounter<long>("ms.messaging.inbox.processed", "{message}");
        _inboxFailed = _meter.CreateCounter<long>("ms.messaging.inbox.failed", "{message}");
        _inboxHandlerDuration = _meter.CreateHistogram<double>("ms.messaging.inbox.handler.duration", "ms");
    }

    public void RecordOutboxClaimed(int count) => _outboxClaimed.Add(count);

    public void RecordOutboxPublished(double durationMilliseconds)
    {
        _outboxPublished.Add(1);
        _outboxPublishDuration.Record(durationMilliseconds, PublishedOutcome);
    }

    public void RecordOutboxFailed(double durationMilliseconds, bool deadLettered)
    {
        _outboxFailed.Add(1);
        if (deadLettered)
        {
            _outboxDeadLettered.Add(1);
        }

        _outboxPublishDuration.Record(durationMilliseconds, deadLettered ? DeadLetteredOutcome : FailedOutcome);
    }

    public void RecordInboxRegistration(bool firstDelivery)
    {
        if (firstDelivery)
        {
            _inboxRegistered.Add(1);
        }
        else
        {
            _inboxDuplicate.Add(1);
        }
    }

    public void RecordInboxShortCircuited() => _inboxShortCircuited.Add(1);

    public void RecordInboxProcessed(double durationMilliseconds)
    {
        _inboxProcessed.Add(1);
        _inboxHandlerDuration.Record(durationMilliseconds, ProcessedOutcome);
    }

    public void RecordInboxFailed(double durationMilliseconds)
    {
        _inboxFailed.Add(1);
        _inboxHandlerDuration.Record(durationMilliseconds, FailedOutcome);
    }

    public void Dispose() => _meter.Dispose();
}
