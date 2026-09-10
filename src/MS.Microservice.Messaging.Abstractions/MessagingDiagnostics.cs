using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MS.Microservice.Messaging;

public sealed class MessagingDiagnostics : IDisposable
{
    public const string Name = "MS.Microservice.Messaging";
    public ActivitySource Activities { get; } = new(Name);
    private readonly Meter _meter = new(Name);
    private readonly ConcurrentDictionary<(string Provider, string Queue, string State), long> depths = new();
    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;

    public MessagingDiagnostics()
    {
        _meter.CreateObservableGauge("messaging.stored_messages", () => depths.Select(pair =>
            new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("provider", pair.Key.Provider),
                new("queue", pair.Key.Queue), new("state", pair.Key.State))), "messages");
        _operations = _meter.CreateCounter<long>("messaging.operations", "operations");
        _duration = _meter.CreateHistogram<double>("messaging.operation.duration", "ms");
    }

    public void Record(string provider, string outcome, double milliseconds, string? consumer = null)
    {
        var tags = new TagList { { "provider", provider }, { "outcome", outcome } };
        if (consumer is not null) tags.Add("consumer", consumer);
        _operations.Add(1, tags);
        _duration.Record(milliseconds, tags);
    }

    public void SetQueueDepth(string provider, string queue, string state, long count)
        => depths[(provider, queue, state)] = count;
    public void Dispose() { Activities.Dispose(); _meter.Dispose(); }
}
