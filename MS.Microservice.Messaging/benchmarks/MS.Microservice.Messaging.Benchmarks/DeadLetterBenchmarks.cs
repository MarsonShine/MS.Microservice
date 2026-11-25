using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.SelfManaged;

internal static class DeadLetterBenchmarks
{
    private const int Limit = 32;
    private const int Rounds = 5;
    private const int Iterations = 5;

    public static async Task RunAsync()
    {
        foreach (var payloadBytes in new[] { 128, 64 * 1024, 128 * 1024 })
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddScoped(_ => new BenchmarkContext(connection));
            services.AddSelfManagedMessaging<BenchmarkContext>(new MessageTopology([], []));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BenchmarkContext>();
            await db.Database.EnsureCreatedAsync();
            var expected = await SeedAsync(db, payloadBytes);
            var failures = scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>();

            // Warm up the EF query and check every returned field before timing.
            var sample = await failures.ListAsync(Limit);
            if (!sample.SequenceEqual(expected))
            {
                throw new InvalidOperationException("The dead-letter listing has unexpected metadata or order.");
            }

            var times = new double[Rounds];
            var allocations = new double[Rounds];
            var count = 0;
            for (var round = 0; round < Rounds; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < Iterations; i++)
                {
                    count += (await failures.ListAsync(Limit)).Count;
                }
                times[round] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Iterations;
                allocations[round] = (double)(GC.GetTotalAllocatedBytes(precise: true) - before) / Iterations;
            }

            if (count != Rounds * Iterations * Limit)
            {
                throw new InvalidOperationException("The number of results changed during measurement.");
            }
            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine($"Dead-letter list, {Limit} rows/table, limit {Limit}, {payloadBytes} B payload: " +
                $"{times[Rounds / 2]:F1} us/list, {allocations[Rounds / 2]:F0} B/list");
        }
    }

    private static async Task<List<FailedMessage>> SeedAsync(BenchmarkContext db, int payloadBytes)
    {
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const string consumer = "audit:%消息";
        // JSON string containing ASCII only, so the UTF-8 byte count is exact.
        var payload = "\"" + new string('x', payloadBytes - 2) + "\"";
        var expected = new List<FailedMessage>(2 * Limit);
        await using var transaction = await db.Database.BeginTransactionAsync();
        for (var i = 0; i < Limit; i++)
        {
            var outgoing = Guid.NewGuid();
            var incoming = Guid.NewGuid();
            var outTime = first.AddSeconds(2 * i);
            var inTime = outTime.AddSeconds(1);
            // State 3 is DeadLettered in both mappings. Setup uses SQL because the mapped entities are internal.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Outbox" ("Id","ContractName","ContractVersion","OccurredAtUtc","Payload","CreatedAtUtc",
                    "State","AttemptCount","NextAttemptAtUtc","CompletedAtUtc","ErrorCode")
                VALUES ({outgoing},'orders.out',1,{outTime.Ticks},{payload},{outTime},3,1,{outTime},{outTime},'send')
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Inbox" ("MessageId","Consumer","ContractName","ContractVersion","OccurredAtUtc","Payload",
                    "ReceivedAtUtc","State","AttemptCount","NextAttemptAtUtc","CompletedAtUtc","ErrorCode")
                VALUES ({incoming},{consumer},'orders.in',2,{inTime.Ticks},{payload},{inTime},3,1,{inTime},{inTime},'consume')
                """);
            expected.Add(new($"outbox:{outgoing:N}", outgoing, "orders.out", 1, null, new DateTimeOffset(outTime), "send"));
            expected.Add(new($"inbox:{incoming:N}:{Uri.EscapeDataString(consumer)}", incoming, "orders.in", 2,
                consumer, new DateTimeOffset(inTime), "consume"));
        }
        await transaction.CommitAsync();
        return expected.GetRange(0, Limit);
    }

    private sealed class BenchmarkContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddSelfManagedMessaging();
    }
}
