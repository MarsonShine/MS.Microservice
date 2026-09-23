using System.Diagnostics.Metrics;
using MS.Microservice.Infrastructure.Telemetry;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Telemetry;

public sealed class PlatformMetricsTests
{
    [Fact]
    public void MessagingMetrics_EmitLowCardinalityCountersAndDurations()
    {
        var longMeasurements = new List<(string Name, long Value)>();
        var doubleMeasurements = new List<(string Name, double Value, string? Outcome)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == PlatformMetrics.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            longMeasurements.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome") outcome = tag.Value?.ToString();
            }

            doubleMeasurements.Add((instrument.Name, value, outcome));
        });
        listener.Start();
        using var metrics = new PlatformMetrics();

        metrics.RecordOutboxClaimed(3);
        metrics.RecordOutboxPublished(12.5);
        metrics.RecordOutboxFailed(20, deadLettered: true);
        metrics.RecordOutboxFailed(21, deadLettered: false);
        metrics.RecordInboxRegistration(firstDelivery: true);
        metrics.RecordInboxRegistration(firstDelivery: false);
        metrics.RecordInboxShortCircuited();
        metrics.RecordInboxProcessed(7);
        metrics.RecordInboxFailed(9);

        Assert.Contains(("ms.messaging.outbox.claimed", 3L), longMeasurements);
        Assert.Contains(("ms.messaging.outbox.published", 1L), longMeasurements);
        Assert.Contains(("ms.messaging.outbox.dead_lettered", 1L), longMeasurements);
        Assert.Contains(("ms.messaging.inbox.duplicate", 1L), longMeasurements);
        Assert.Contains(doubleMeasurements, measurement =>
            measurement.Name == "ms.messaging.outbox.publish.duration"
            && measurement.Value == 12.5
            && measurement.Outcome == "published");
        Assert.Contains(doubleMeasurements, measurement =>
            measurement.Name == "ms.messaging.outbox.publish.duration"
            && measurement.Value == 20
            && measurement.Outcome == "dead_lettered");
        Assert.Contains(doubleMeasurements, measurement =>
            measurement.Name == "ms.messaging.outbox.publish.duration"
            && measurement.Value == 21
            && measurement.Outcome == "failed");
        Assert.Contains(doubleMeasurements, measurement =>
            measurement.Name == "ms.messaging.inbox.handler.duration"
            && measurement.Value == 7
            && measurement.Outcome == "processed");
        Assert.Contains(doubleMeasurements, measurement =>
            measurement.Name == "ms.messaging.inbox.handler.duration"
            && measurement.Value == 9
            && measurement.Outcome == "failed");
    }
}
