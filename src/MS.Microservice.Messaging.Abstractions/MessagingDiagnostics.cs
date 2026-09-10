using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MS.Microservice.Messaging;

public sealed class MessagingDiagnostics : IDisposable
{
    public const string Name = "MS.Microservice.Messaging";
    public ActivitySource Activities { get; } = new(Name);
    private readonly Meter _meter = new(Name);
    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;

    public MessagingDiagnostics()
    {
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

    public void Dispose() { Activities.Dispose(); _meter.Dispose(); }
}
