using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Persistence.Auditing.Benchmarks;
using MS.Microservice.Persistence.EFCore.Auditing;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("AUDIT_BENCHMARK_CONNECTION")
    ?? throw new InvalidOperationException("Set AUDIT_BENCHMARK_CONNECTION outside the repository.");
if (args.Contains("--initialize"))
{
    var target = new NpgsqlConnectionStringBuilder(connectionString);
    var database = target.Database ?? throw new InvalidOperationException("Specify the dedicated test database.");
    target.Database = "postgres";
    await using var maintenance = new NpgsqlConnection(target.ConnectionString);
    await maintenance.OpenAsync();
    await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_database WHERE datname = @database)", maintenance);
    exists.Parameters.AddWithValue("database", database);
    if (!(bool)(await exists.ExecuteScalarAsync())!)
    {
        await using var create = new NpgsqlCommand("CREATE DATABASE " + new NpgsqlCommandBuilder().QuoteIdentifier(database), maintenance);
        await create.ExecuteNonQueryAsync();
        Console.WriteLine("Dedicated test database created; it will not be dropped by this program.");
    }
    return;
}
await using var admin = new NpgsqlConnection(connectionString);
await admin.OpenAsync();
Console.WriteLine($"PostgreSQL {admin.PostgreSqlVersion}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}");
await using var durabilityCommand = new NpgsqlCommand("SELECT json_object_agg(name, setting)::text FROM pg_settings WHERE name IN ('synchronous_commit','fsync','full_page_writes','wal_sync_method','track_wal_io_timing','synchronous_standby_names')", admin);
var durability = JsonSerializer.Deserialize<JsonElement>((string)(await durabilityCommand.ExecuteScalarAsync())!);
Console.WriteLine("Durability settings: " + durability);
if (args.Contains("--probe")) return;

// Only this randomly named schema is created and removed; the database and existing schemas are never dropped.
var schema = "ms_audit_bench_" + Guid.NewGuid().ToString("N");
var owned = false;
try
{
    await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
    owned = true;
    Console.WriteLine($"Temporary schema: {schema}");
    var counter = new CommandCounter();
    var plain = Configure(new DbContextOptionsBuilder<PlainContext>()).Options;
    var audited = Configure(new DbContextOptionsBuilder<CapturingContext>()).Options;
    DbContextOptionsBuilder<T> Configure<T>(DbContextOptionsBuilder<T> builder) where T : DbContext => builder
        .UseNpgsql(connectionString).ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>().AddInterceptors(counter);
    DbContext Context(Variant variant, AuditOperationContext operation) => variant == Variant.Plain
        ? new PlainContext(plain, schema)
        : new CapturingContext(audited, schema, operation) { AuditingEnabled = variant == Variant.Enabled };

    await using (var setup = new PlainContext(plain, schema))
    {
        await setup.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        for (var worker = 0; worker < 4; worker++)
        {
            for (var number = 0; number < 1000; number++)
            {
                var row = new Order { Id = Guid.NewGuid(), Worker = worker, Number = number };
                setup.Add(row);
                if (number == 0) setup.AddRange(new Stock { Id = Guid.NewGuid(), OrderId = row.Id }, new Stock { Id = Guid.NewGuid(), OrderId = row.Id });
            }
        }
        await setup.SaveChangesAsync();
    }

    async Task<Phases> Execute(Scenario scenario, Variant variant, int worker, bool rollback = false)
    {
        var operation = new AuditOperationContext();
        using var scope = variant == Variant.Enabled ? operation.Begin("benchmark", "worker", scenario.ToString()) : null;
        await using var context = Context(variant, operation);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var rows = scenario == Scenario.Tracked1000
            ? await context.Set<Order>().Where(value => value.Worker == worker).OrderBy(value => value.Number).ToListAsync()
            : await context.Set<Order>().Where(value => value.Worker == worker && value.Number == 0).ToListAsync();
        var row = rows[0];
        if (scenario == Scenario.Unselected) row.LastSeen++;
        else if (scenario != Scenario.Unchanged)
        {
            row.Quantity++; row.Amount += 0.5m; row.Approved = !row.Approved;
            if (scenario == Scenario.MultiTable)
            {
                var stock = await context.Set<Stock>().Where(value => value.OrderId == row.Id).ToListAsync();
                foreach (var value in stock) value.Available--;
                context.Add(new Approval { Id = Guid.NewGuid(), OrderId = row.Id });
            }
        }
        var saveStarted = Stopwatch.GetTimestamp();
        var beforeCallbackMs = 0d;
        context.SavingChanges += (_, _) => beforeCallbackMs = Stopwatch.GetElapsedTime(saveStarted).TotalMilliseconds;
        await context.SaveChangesAsync();
        var saveMs = Stopwatch.GetElapsedTime(saveStarted).TotalMilliseconds;
        var commitStarted = Stopwatch.GetTimestamp();
        if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
        return new(beforeCallbackMs, saveMs, Stopwatch.GetElapsedTime(commitStarted).TotalMilliseconds);
    }

    async Task<long> AuditCount()
    {
        await using var context = new PlainContext(plain, schema);
        return await context.Set<AuditRecord>().LongCountAsync();
    }
    // Run the atomicity check on the acceptance provider before measuring it.
    await Execute(Scenario.MultiTable, Variant.Enabled, 0, rollback: true);
    await using (var verify = new PlainContext(plain, schema))
    {
        if (await AuditCount() != 0 || await verify.Set<Approval>().AnyAsync()
            || await verify.Set<Order>().AnyAsync(value => value.Quantity != 1)
            || await verify.Set<Stock>().AnyAsync(value => value.Available != 100000))
            throw new InvalidOperationException("PostgreSQL rollback did not preserve business/audit atomicity.");
    }
    await Execute(Scenario.MultiTable, Variant.Enabled, 0);
    if (await AuditCount() != 4) throw new InvalidOperationException("PostgreSQL commit must contain four audit details.");
    Console.WriteLine("PostgreSQL multi-table commit and rollback: passed.");

    string? Argument(string name) => args.SkipWhile(value => value != name).Skip(1).FirstOrDefault();
    const int rounds = 3, warmup = 24, samplesPerWorker = 96;
    var scenarios = Argument("--scenarios") is { } chosen ? chosen.Split(',').Select(Enum.Parse<Scenario>).ToArray() : Enum.GetValues<Scenario>();
    var concurrencies = Argument("--concurrency") is { } workers ? workers.Split(',').Select(int.Parse).ToArray() : [1, 4];
    if (concurrencies.Any(value => value is < 1 or > 4)) throw new ArgumentOutOfRangeException(nameof(concurrencies), "Use 1–4 workers.");
    var results = new List<Measurement>();
    foreach (var scenario in scenarios)
    foreach (var concurrency in concurrencies)
    {
        var accumulated = Enum.GetValues<Variant>().ToDictionary(value => value, _ => new Accumulator());
        foreach (var variant in Enum.GetValues<Variant>())
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
            { for (var sample = 0; sample < warmup; sample++) await Execute(scenario, variant, worker); }));
        for (var round = 0; round < rounds; round++)
        for (var offset = 0; offset < 3; offset++)
        {
            var variant = (Variant)((round + offset) % 3);
            var auditBefore = await AuditCount();
            var commandsBefore = counter.Count;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            var times = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
            {
                var elapsed = new Sample[samplesPerWorker];
                for (var sample = 0; sample < samplesPerWorker; sample++)
                {
                    var started = Stopwatch.GetTimestamp();
                    var phases = await Execute(scenario, variant, worker);
                    elapsed[sample] = new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, phases);
                }
                return elapsed;
            }));
            stopwatch.Stop();
            var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var commands = counter.Count - commandsBefore;
            var auditAfter = await AuditCount();
            var expected = variant == Variant.Enabled ? scenario switch
            { Scenario.Single or Scenario.Tracked1000 => 1, Scenario.MultiTable => 4, _ => 0 } : 0;
            if (auditAfter - auditBefore != expected * samplesPerWorker * concurrency)
                throw new InvalidOperationException("The measured variant wrote an unexpected number of audit records.");
            var totals = accumulated[variant];
            totals.Times.AddRange(times.SelectMany(value => value));
            totals.Seconds += stopwatch.Elapsed.TotalSeconds; totals.Bytes += bytes; totals.Commands += commands;
        }
        foreach (var (variant, totals) in accumulated)
        {
            var times = totals.Times.Select(value => value.Ms).Order().ToArray();
            double Percentile(double fraction) => times[(int)Math.Ceiling(times.Length * fraction) - 1];
            var result = new Measurement(scenario.ToString(), concurrency, variant.ToString(), times.Length,
                times.Length / totals.Seconds, Percentile(.50), Percentile(.95), Percentile(.99),
                totals.Bytes / times.Length, (double)totals.Commands / times.Length,
                totals.Times.Average(value => value.Phases.BeforeCallbackMs), totals.Times.Average(value => value.Phases.SaveMs),
                totals.Times.Average(value => value.Phases.CommitMs));
            results.Add(result);
            Console.WriteLine($"{scenario,-12} c={concurrency} {variant,-8} {result.OperationsPerSecond:F1} ops/s P95={result.P95Ms:F3}ms commands={result.CommandsPerOperation:F1}");
        }
    }
    var output = Argument("--output") ?? "audit-benchmark.json";
    var report = new
    {
        MeasuredAtUtc = DateTimeOffset.UtcNow, PostgreSql = admin.PostgreSqlVersion.ToString(),
        Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
        ProcessorCount = Environment.ProcessorCount, Rounds = rounds, WarmupPerWorkerPerVariant = warmup,
        PostgreSqlDurability = durability,
        SamplesPerWorkerPerRound = samplesPerWorker, AtomicityCheck = "passed", Results = results,
        Notes = "Latency includes context/scope creation, tracking query, explicit transaction, save, commit and disposal. EF command count excludes transaction protocol. Allocations are approximate process-wide deltas. BeforeCallback includes audit collection/DetectChanges only in Enabled; plain EF detects later. Save includes database execution and is not pure CPU time. Not HTTP latency."
    };
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Report: {Path.GetFullPath(output)}");
}
finally
{
    if (owned)
    {
        await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
        Console.WriteLine("Temporary benchmark schema removed.");
    }
}

internal enum Variant { Plain, Disabled, Enabled }
internal enum Scenario { Unchanged, Unselected, Single, MultiTable, Tracked1000 }
internal sealed class Accumulator
{
    internal List<Sample> Times { get; } = [];
    internal double Seconds; internal long Bytes; internal long Commands;
}
internal sealed record Measurement(string Scenario, int Concurrency, string Variant, int Samples,
    double OperationsPerSecond, double P50Ms, double P95Ms, double P99Ms, long AllocatedBytesPerOperation, double CommandsPerOperation,
    double MeanBeforeCallbackMs, double MeanSaveMs, double MeanCommitMs);
internal sealed record Phases(double BeforeCallbackMs, double SaveMs, double CommitMs);
internal sealed record Sample(double Ms, Phases Phases);
internal sealed class CommandCounter : DbCommandInterceptor
{
    private long _count;
    internal long Count => Interlocked.Read(ref _count);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref _count); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref _count); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref _count); return ValueTask.FromResult(result); }
}
