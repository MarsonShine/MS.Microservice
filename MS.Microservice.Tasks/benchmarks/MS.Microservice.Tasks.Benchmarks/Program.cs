using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Tasks;
using MS.Microservice.Tasks.EFCore;

const int jobsPerRound = 32;
const int warmupRounds = 2;
const int measuredRounds = 7;
const string scope = "bench-owner";
foreach (var payloadBytes in new[] { 32, 128 * 1024 })
{
    var payload = "\"" + new string('x', payloadBytes - 2) + "\"";
    var times = new List<double>();
    var allocations = new List<double>();
    for (var round = 0; round < warmupRounds + measuredRounds; round++)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<BenchContext>(options => options.UseSqlite(connection));
        services.AddEfCoreJobStore<BenchContext>();
        await using var provider = services.BuildServiceProvider();
        await using var serviceScope = provider.CreateAsyncScope();
        await serviceScope.ServiceProvider.GetRequiredService<BenchContext>().Database.EnsureCreatedAsync();
        var store = serviceScope.ServiceProvider.GetRequiredService<IJobStore>();
        var attempts = new JobAttemptToken[jobsPerRound];
        for (var i = 0; i < attempts.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            await store.SubmitAsync(new JobSubmission(id, scope, "export", 1, payload));
            var execution = await store.TryStartAsync(scope, id) ?? throw new InvalidOperationException("Claim failed.");
            attempts[i] = execution.Attempt;
        }

        var before = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        foreach (var attempt in attempts)
        {
            if (!await store.CompleteAsync(attempt, "result"))
            {
                throw new InvalidOperationException("Completion failed.");
            }
        }
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        if (round >= warmupRounds)
        {
            times.Add(elapsed.TotalMicroseconds / jobsPerRound);
            allocations.Add((double)allocated / jobsPerRound);
        }

        foreach (var attempt in attempts)
        {
            var job = await store.GetAsync(scope, attempt.JobId);
            var history = await store.GetAttemptsAsync(scope, attempt.JobId);
            if (job is not { State: JobState.Succeeded, Progress: 100, ResultReference: "result" }
                || job.PayloadJson != payload || history.Count != 1
                || history[0] is not { State: JobState.Succeeded, Progress: 100, ResultReference: "result" })
            {
                throw new InvalidOperationException("The job or its attempt history differs from the expected outcome.");
            }
        }
    }

    times.Sort();
    allocations.Sort();
    Console.WriteLine($"JSON {payloadBytes} B: {times[measuredRounds / 2]:F1} us/op, {allocations[measuredRounds / 2]:F0} B/op (median {measuredRounds} x {jobsPerRound})");
}

sealed class BenchContext(DbContextOptions<BenchContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTaskCenter();
}
