using System.Diagnostics;
using MS.Microservice.Messaging;

const int iterations = 200_000;
const int rounds = 5;

foreach (var size in new[] { 1, 2, 8 })
{
    var subscriptions = Enumerable.Range(0, size)
        .Select(index => MessageSubscription.For<Changed, Handler>($"consumer-{index}"));
    var topology = new MessageTopology([MessageContract.For<Changed>("changed")], subscriptions);
    Measure($"{size} subscription(s), first", topology, "consumer-0");
    if (size > 1) Measure($"{size} subscription(s), last", topology, $"consumer-{size - 1}");
}

static void Measure(string name, MessageTopology topology, string consumer)
{
    for (var i = 0; i < iterations / 10; i++) topology.Subscription(consumer);
    var times = new double[rounds];
    var allocations = new double[rounds];
    var checksum = 0;
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) checksum += topology.Subscription(consumer).Consumer.Length;
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op, checksum {checksum}");
}

public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
public sealed class Handler : IIntegrationEventHandler<Changed>
{
    public Task HandleAsync(Changed message, MessageContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
