using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Persistence.Auditing.Benchmarks;

internal static class SqliteCapture
{
    private const int Warmups = 20;
    private const int Rounds = 7;
    private const int Iterations = 40;

    internal static void Run()
    {
        foreach (var rows in new[] { 1, 32 })
        {
            Measure(rows);
        }
    }

    private static void Measure(int rows)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CapturingContext>().UseSqlite(connection).Options;
        var operation = new AuditOperationContext();
        using var scope = operation.Begin("benchmark", "worker", "edit");
        using var setup = new CapturingContext(options, "benchmark", operation) { AuditingEnabled = false };
        setup.Database.EnsureCreated();
        var order = new Order { Id = Guid.NewGuid() };
        setup.Add(order);
        for (var i = 0; i < rows; i++)
        {
            setup.Add(new Approval { Id = Guid.NewGuid(), OrderId = order.Id });
        }
        setup.SaveChanges();

        var notes = new[] { new string('a', 512), new string('b', 512) };
        var times = new double[Rounds];
        var allocations = new double[Rounds];
        for (var i = 0; i < Warmups + Rounds * Iterations; i++)
        {
            using var db = new CapturingContext(options, "benchmark", operation);
            foreach (var row in db.Set<Approval>().ToList())
            {
                row.Note = notes[i % notes.Length];
            }

            var started = 0L;
            var allocatedBefore = 0L;
            db.SavingChanges += (_, _) =>
            {
                if (i >= Warmups)
                {
                    var round = (i - Warmups) / Iterations;
                    times[round] += Stopwatch.GetElapsedTime(started).TotalMicroseconds / Iterations;
                    allocations[round] += (double)(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / Iterations;
                }
            };

            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            if (db.SaveChanges() != 2 * rows)
            {
                throw new InvalidOperationException("Expected one business update and one audit insert per row.");
            }
        }
        if (setup.Set<AuditRecord>().Count() != (Warmups + Rounds * Iterations) * rows)
        {
            throw new InvalidOperationException("Stored audit count does not match the measured workload.");
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{rows} row(s): {times[Rounds / 2]:F2} us/save, {allocations[Rounds / 2]:F0} B/save");
    }
}
