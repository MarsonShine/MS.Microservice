using System.Diagnostics;
using System.Text;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;

internal static class WireBenchmarks
{
    private const int MaxBytes = 1024 * 1024;

    public static void Run()
    {
        const string name = "profile.changed";
        var unicodeName = new string('中', 80) + "ab"; // 254 bytes with .v2147483647
        Measure("routing key, ASCII", 200_000, () => MessageRoutingKey.IsValid(name, 1) ? 1 : 0, 1);
        Measure("routing key, 254 UTF-8 bytes", 200_000,
            () => MessageRoutingKey.IsValid(unicodeName, int.MaxValue) ? 1 : 0, 1);

        var small = new SerializedMessage(Guid.Parse("003f792b-6f32-41dc-9a32-777b6b059973"), name, 1,
            DateTimeOffset.UnixEpoch, """{"value":"ok"}""", "correlation");
        MeasureEncoded("encode, small ASCII", 40_000, small);
        var large = small with { Payload = "\"" + new string('é', (MaxBytes - 2) / 2) + "\"" };
        MeasureEncoded("encode, 1 MiB valid UTF-8", 32, large);
        var oversized = small with { Payload = "\"" + new string('中', (MaxBytes - 2) / 3 + 1) + "\"" };
        if (Encoding.UTF8.GetByteCount(oversized.Payload) != MaxBytes + 1)
        {
            throw new InvalidOperationException("The oversized payload must cross the byte limit by one.");
        }
        Measure("encode, 1 MiB + 1 byte UTF-8 rejection", 32, () =>
        {
            try
            {
                _ = RabbitMqWireCodec.Encode(oversized, MaxBytes);
                return 0;
            }
            catch (PermanentMessageException ex) when (ex.Message == "message_limits_exceeded")
            {
                return 1;
            }
        }, 1);
    }

    private static void MeasureEncoded(string name, int iterations, SerializedMessage message)
    {
        var expected = Encoding.UTF8.GetBytes(message.Payload);
        var encoded = RabbitMqWireCodec.Encode(message, MaxBytes);
        if (!encoded.Body.AsSpan().SequenceEqual(expected) || encoded.Properties.Type != "profile.changed.v1" ||
            encoded.Properties.MessageId != message.Id.ToString("N") || !encoded.Properties.Persistent)
        {
            throw new InvalidOperationException($"Unexpected wire output for {name}.");
        }
        Measure(name, iterations, () => RabbitMqWireCodec.Encode(message, MaxBytes).Body.Length, expected.Length);
    }

    private static void Measure(string name, int iterations, Func<int> operation, int expected)
    {
        const int rounds = 5;
        for (var i = 0; i < Math.Max(8, iterations / 10); i++)
        {
            _ = operation();
        }

        var times = new double[rounds];
        var allocations = new double[rounds];
        for (var round = 0; round < rounds; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            long observed = 0;
            for (var i = 0; i < iterations; i++)
            {
                observed += operation();
            }
            times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
            if (observed != (long)expected * iterations)
            {
                throw new InvalidOperationException($"Unexpected result for {name}: {observed}.");
            }
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F0} B/op (median of {rounds} x {iterations})");
    }
}
