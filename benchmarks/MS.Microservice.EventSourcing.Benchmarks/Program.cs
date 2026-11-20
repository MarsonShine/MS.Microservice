using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Repository;
using MS.Microservice.Infrastructure.EventSourcing.Serialization;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var context = new EventStoreDbContext(new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(connection).Options);
await context.Database.EnsureCreatedAsync();
var serializer = new SystemTextJsonEventSerializer(new EventTypeRegistry().Register<QuantityChanged>());
var store = new PostgresEventStore(context, serializer);
var history = new List<EventEnvelope<QuantityChanged>>();
for (var index = 1; index <= 1024; index++)
{
    bool shipment = index % 4 == 0;
    var envelope = new EventEnvelope<QuantityChanged>(
        new Guid(index, 0, 0, new byte[8]), shipment ? "shipment-1" : "stock-1",
        shipment ? "shipment" : "stock", shipment ? index / 4 : index - index / 4, index * 3,
        new QuantityChanged(index), new EventMetadata(CorrelationId: $"request-{index}"),
        DateTimeOffset.UnixEpoch.AddSeconds(index));
    history.Add(envelope);
    var encoded = serializer.Serialize(envelope.Data, envelope.Metadata);
    context.Add(new EventStoreRecord
    {
        GlobalPosition = envelope.GlobalPosition, EventId = envelope.EventId, StreamId = envelope.StreamId,
        StreamType = envelope.StreamType, Version = envelope.Version, CreatedAt = envelope.CreatedAt,
        EventType = encoded.EventType, Payload = encoded.Payload, Metadata = encoded.Metadata
    });
}
await context.SaveChangesAsync();
context.ChangeTracker.Clear();

await Measure("LoadStream (from v257)", 8, () => store.LoadStreamAsync<QuantityChanged>("stock-1", 257),
    history.Where(e => e.StreamId == "stock-1" && e.Version >= 257).ToArray());
await Measure("ReadAll (1024)", 4, () => store.ReadAllAsync<QuantityChanged>(0), history);
var afterCursor = history.Where(e => e.GlobalPosition > 27 && e.StreamType == "stock");
foreach (int take in new[] { 1, 100 })
{
    await Measure($"ReadBatch ({take})", take == 1 ? 128 : 24,
        () => store.ReadBatchAsync<QuantityChanged>(27, take, "stock"), afterCursor.Take(take).ToArray());
}
for (var start = 0; start < history.Count; start += 32)
{
    var metadata = new EventMetadata(CorrelationId: $"batch-{start / 32}", UserId: "staff", TenantId: "store");
    var json = serializer.Serialize(history[start].Data, metadata).Metadata;
    var first = history[start].GlobalPosition;
    var last = history[start + 31].GlobalPosition;
    await context.EventStore.Where(row => row.GlobalPosition >= first && row.GlobalPosition <= last)
        .ExecuteUpdateAsync(set => set.SetProperty(row => row.Metadata, json));
    for (var i = start; i < start + 32; i++) history[i] = history[i] with { Metadata = metadata };
}
await Measure("ReadAll (metadata shared per 32)", 4, () => store.ReadAllAsync<QuantityChanged>(0), history);
var defaultMetadata = new EventMetadata();
var defaultJson = serializer.Serialize(history[0].Data, defaultMetadata).Metadata;
await context.EventStore.ExecuteUpdateAsync(set => set.SetProperty(row => row.Metadata, defaultJson));
for (var i = 0; i < history.Count; i++) history[i] = history[i] with { Metadata = defaultMetadata };
await Measure("ReadAll (default metadata)", 4, () => store.ReadAllAsync<QuantityChanged>(0), history);
if (context.ChangeTracker.Entries().Any()) throw new InvalidOperationException("The read paths tracked entities.");

static async Task Measure(string name, int iterations, Func<Task<IReadOnlyList<EventEnvelope<QuantityChanged>>>> read,
    IReadOnlyList<EventEnvelope<QuantityChanged>> expected)
{
    void Check(IReadOnlyList<EventEnvelope<QuantityChanged>> result)
    {
        if (!result.SequenceEqual(expected)) throw new InvalidOperationException(name);
    }

    for (var i = 0; i < iterations; i++) Check(await read());
    var microseconds = new double[7];
    var allocations = new double[7];
    for (var round = 0; round < microseconds.Length; round++)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var start = Stopwatch.GetTimestamp();
        var result = await read();
        for (var i = 1; i < iterations; i++) result = await read();
        microseconds[round] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
        allocations[round] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)iterations;
        Check(result);
    }
    Array.Sort(microseconds);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {microseconds[3]:F2} us/op, {allocations[3]:F0} B/op");
}

internal sealed record QuantityChanged(int Quantity) : IEventSourcedEvent;
