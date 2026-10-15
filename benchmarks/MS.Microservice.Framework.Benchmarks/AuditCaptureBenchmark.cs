using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Core.Security.SensitiveData;
using MS.Microservice.Persistence.EFCore.Auditing;

internal static class AuditCaptureBenchmark
{
    internal static CaptureResult[] Run()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var operation = new AuditOperationContext();
        using var context = new CaptureContext(new DbContextOptionsBuilder<CaptureContext>().UseSqlite(connection).Options, operation);
        context.AuditingEnabled = false; context.Database.EnsureCreated();
        context.AddRange(new PlainRow { Id = 1 }, new ProtectedRow { Id = 1 }); context.SaveChanges();
        context.ChangeTracker.Clear(); context.AuditingEnabled = true;
        var plain = context.Set<PlainRow>().Single(); var protectedRow = context.Set<ProtectedRow>().Single();
        using var scope = operation.Begin("benchmark", "worker", "change");
        long started = 0, allocated = 0; double ns = 0, bytes = 0;
        context.SavingChanges += (_, _) =>
        {
            ns = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            throw new StopBeforeSql();
        };
        void Capture(bool protect)
        {
            if (protect) { protectedRow.Phone = "13899118000"; protectedRow.Email = "bob@example.com"; protectedRow.InternalNote = "updated"; }
            else { plain.Phone = "13899118000"; plain.Email = "bob@example.com"; plain.InternalNote = "updated"; }
            started = Stopwatch.GetTimestamp(); allocated = GC.GetAllocatedBytesForCurrentThread();
            try { context.SaveChanges(); throw new InvalidOperationException("Benchmark must stop before SQL."); }
            catch (StopBeforeSql) { }
            // The next capture must contain one changed row, not both variants.
            context.Entry(protect ? (object)protectedRow : plain).State = EntityState.Unchanged;
        }
        var result = new List<CaptureResult>();
        // Restore known originals on each iteration without a database query.
        foreach (var protect in new[] { false, true })
        {
            var times = new List<double>(); var allocations = new List<double>();
            for (var iteration = 0; iteration < 2200; iteration++)
            {
                var entry = context.Entry(protect ? (object)protectedRow : plain);
                entry.Property("Phone").OriginalValue = "13800138000";
                entry.Property("Email").OriginalValue = "alice@example.com";
                entry.Property("InternalNote").OriginalValue = "original";
                Capture(protect);
                if (iteration >= 200) { times.Add(ns); allocations.Add(bytes); }
            }
            result.Add(new(protect ? "protected" : "plain", times.Order().ElementAt(times.Count / 2), allocations.Order().ElementAt(allocations.Count / 2)));
        }
        if (context.Set<AuditRecord>().Count() != 0) throw new InvalidOperationException("Capture benchmark unexpectedly wrote audit rows.");
        return result.ToArray();
    }
    private sealed class StopBeforeSql : Exception;
    private sealed class CaptureContext(DbContextOptions<CaptureContext> options, AuditOperationContext operation) : AuditedDbContext(options, operation)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.AddAuditRecords();
            model.Entity<PlainRow>().HasKey(row => row.Id); model.Entity<PlainRow>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<PlainRow>().AuditChanges("plain", row => new { row.Phone, row.Email, row.InternalNote });
            model.Entity<ProtectedRow>().HasKey(row => row.Id); model.Entity<ProtectedRow>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<ProtectedRow>().AuditChanges("protected", row => new { row.Phone, row.Email, row.InternalNote });
        }
    }
    private sealed class PlainRow
    {
        public int Id { get; set; } public string Phone { get; set; } = "13800138000";
        public string Email { get; set; } = "alice@example.com"; public string InternalNote { get; set; } = "original";
    }
    private sealed class ProtectedRow
    {
        public int Id { get; set; }
        [Sensitive(SensitiveKind.Phone)] public string Phone { get; set; } = "13800138000";
        [Sensitive(SensitiveKind.Email)] public string Email { get; set; } = "alice@example.com";
        [Sensitive(SensitiveKind.Secret)] public string InternalNote { get; set; } = "original";
    }
}
internal sealed record CaptureResult(string Variant, double MedianNs, double MedianBytes);