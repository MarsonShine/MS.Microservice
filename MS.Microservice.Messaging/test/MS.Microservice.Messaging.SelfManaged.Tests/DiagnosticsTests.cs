using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

[CollectionDefinition("messaging-metrics", DisableParallelization = true)]
public sealed class MetricsCollection;

[Collection("messaging-metrics")]
public sealed class DiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerMetricsDistinguishCommittedWorkFromInvalidMessages(bool invalid)
    {
        var outcomes = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == MessagingDiagnostics.Name && instrument.Name == "messaging.operations")
                meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? consumer = null, outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "consumer") consumer = tag.Value?.ToString();
                if (tag.Key == "outcome") outcome = tag.Value?.ToString();
            }
            if (consumer == "audit") outcomes.Add(outcome!);
        });
        listener.Start();
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BusinessContext>().Database.EnsureCreatedAsync();
        var message = Registry().Serialize(NewEvent());
        if (invalid) message = message with { Payload = "{" };
        var result = await provider.GetRequiredService<IMessageReceiver>().ReceiveAsync(message, "audit", default);
        Assert.Equal(invalid ? DeliveryResult.Reject : DeliveryResult.Acknowledge, result);
        Assert.Equal(invalid ? "dead_letter" : "consumed", Assert.Single(outcomes));
        using var check = provider.CreateScope();
        Assert.Equal(invalid ? 0 : 1, await check.ServiceProvider.GetRequiredService<BusinessContext>().Set<BusinessRow>().CountAsync());
    }

    [Fact]
    public async Task BacklogSnapshotExcludesCompletedRowsAndReportsZerosAfterDrain()
    {
        using var diagnostics = new MessagingDiagnostics();
        using var listener = new MeterListener();
        var values = new Dictionary<string, long>();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == MessagingDiagnostics.Name && instrument.Name == "messaging.stored_messages")
                meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? queue = null, state = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "queue") queue = tag.Value?.ToString();
                if (tag.Key == "state") state = tag.Value?.ToString();
            }
            values[$"{queue}.{state}"] = value;
        });
        listener.Start();
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var pending = OutboxEntry.From(Registry().Serialize(NewEvent()), DateTime.UtcNow);
        var published = OutboxEntry.From(Registry().Serialize(NewEvent()), DateTime.UtcNow);
        published.State = OutboxState.Published;
        context.AddRange(pending, published);
        await context.SaveChangesAsync();
        await StorageMetrics.CaptureAsync(context, diagnostics, default);
        listener.RecordObservableInstruments();
        Assert.Equal(1, values["outbox.Pending"]);
        Assert.Equal(0, values["outbox.DeadLettered"]);
        Assert.DoesNotContain("outbox.Published", values.Keys);
        pending.State = OutboxState.Published;
        await context.SaveChangesAsync();
        await StorageMetrics.CaptureAsync(context, diagnostics, default);
        listener.RecordObservableInstruments();
        Assert.Equal(0, values["outbox.Pending"]);
    }
}
