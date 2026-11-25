using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Idempotency.EFCore;

var verifyOnly = args is ["--verify-only"];
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
using var db = new BenchmarkContext(connection);
db.Database.EnsureCreated();
var store = new EfCoreIdempotencyStore<BenchmarkContext>(db, TimeProvider.System);
var largeRequest = new byte[1024 * 1024];
new Random(42).NextBytes(largeRequest);
var shortRequest = "{\"id\":123}"u8.ToArray();
const string operation = "orders.create";
var createCases = new[]
{
    (Name: "short", Actor: "actor-1", Key: "create-1", Body: shortRequest, Iterations: 2000),
    (Name: "960-byte actor + 128-byte key", Actor: string.Concat(Enumerable.Repeat("顾客🚀", 96)),
        Key: new string('k', 128), Body: shortRequest, Iterations: 1000),
    (Name: "1 MiB body", Actor: "actor-1", Key: "create-large", Body: largeRequest, Iterations: 24)
};

foreach (var item in createCases)
{
    IdempotencyRequest Create() => IdempotencyRequest.Create(operation, item.Actor, item.Key, item.Body);
    var request = Create();
    await StoreAsync(request, new IdempotencyResponse(204, null, []));
    var scopeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + "\0" + item.Actor)));
    var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Key)));
    var expectedRequestHash = Convert.ToHexString(SHA256.HashData(item.Body));
    var storedHash = await db.Set<IdempotencyRecord>().AsNoTracking()
        .Where(row => row.ScopeHash == scopeHash && row.KeyHash == keyHash)
        .Select(row => row.RequestHash).SingleAsync();
    if (storedHash != expectedRequestHash)
    {
        throw new InvalidOperationException($"Stored hashes differ for {item.Name}.");
    }
    Measure($"Create {item.Name}", item.Iterations, Create);
}

foreach (var bodySize in new[] { 64, 65_536 })
{
    var key = $"replay-{bodySize}";
    var replay = IdempotencyRequest.Create(operation, "actor-1", key, "original"u8);
    var different = IdempotencyRequest.Create(operation, "actor-1", key, "different"u8);
    var missing = IdempotencyRequest.Create(operation, "actor-1", "missing-" + key, "original"u8);
    var body = new byte[bodySize];
    new Random(bodySize).NextBytes(body);
    const string location = "/orders/123";
    const string contentType = "application/octet-stream";
    await StoreAsync(replay, new IdempotencyResponse(201, contentType, body, location));

    if ((await store.FindResultAsync(missing)).Value is not IdempotencyMissing ||
        (await store.FindResultAsync(different)).Value is not IdempotencyDifferentRequest)
    {
        throw new InvalidOperationException("Unexpected lookup outcome for a missing or conflicting request.");
    }
    if ((await store.FindResultAsync(replay)).Value is not IdempotencyResponse response ||
        response.StatusCode != 201 || response.ContentType != contentType || response.Location != location ||
        !response.Body.Span.SequenceEqual(body))
    {
        throw new InvalidOperationException("Replay failed to return the exact HTTP response.");
    }

    foreach (var (name, request) in new[] { ("Missing", missing), ("Replay", replay), ("DifferentRequest", different) })
    {
        Measure($"FindResultAsync {name}, stored body {bodySize} B", 100,
            () => store.FindResultAsync(request).GetAwaiter().GetResult());
    }
}
Console.WriteLine("Verified all three request digests, lookup cases, response metadata and complete bodies.");

async Task StoreAsync(IdempotencyRequest request, IdempotencyResponse response)
{
    await using var transaction = await db.Database.BeginTransactionAsync();
    await store.ClaimAndExecuteAsync(request, TimeSpan.FromHours(1), _ => Task.FromResult(response));
    await transaction.CommitAsync();
    db.ChangeTracker.Clear();
}

void Measure<T>(string name, int iterations, Func<T> run)
{
    if (verifyOnly)
    {
        return;
    }
    const int rounds = 7;
    for (var i = 0; i < Math.Max(10, iterations / 5); i++)
    {
        run();
    }
    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var start = Stopwatch.GetTimestamp();
        T last = default!;
        for (var i = 0; i < iterations; i++)
        {
            last = run();
        }
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
        GC.KeepAlive(last);
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op");
}

internal sealed class BenchmarkContext(SqliteConnection connection) : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddHttpIdempotency();
}
