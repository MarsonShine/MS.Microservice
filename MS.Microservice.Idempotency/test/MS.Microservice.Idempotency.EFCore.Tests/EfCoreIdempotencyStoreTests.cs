using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MS.Microservice.Idempotency.EFCore.Tests;

public sealed class EfCoreIdempotencyStoreTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimBusinessWriteAndResponseCommitTogether_AndReplayDoesNotRunBusinessAgain()
    {
        await using var database = await Database.OpenAsync();
        var clock = new ManualClock();
        var request = Request("first");
        var calls = 0;
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var store = new EfCoreIdempotencyStore<TestContext>(context, clock);
            var response = await store.ClaimAndExecuteAsync(request, TimeSpan.FromHours(1), _ =>
            {
                calls++;
                context.BusinessRows.Add(new BusinessRow { Id = 1 });
                return Task.FromResult(new IdempotencyResponse(201, "application/json", "{\"id\":1}"u8,
                    "/api/v1/profiles/1"));
            });
            Assert.Equal(201, response.StatusCode);
            await transaction.CommitAsync();
        }

        await using (var context = database.CreateContext())
        {
            var replay = await new EfCoreIdempotencyStore<TestContext>(context, clock).FindAsync(request);
            Assert.Equal(IdempotencyLookupKind.Replay, replay.Kind);
            Assert.Equal(201, replay.Response!.StatusCode);
            Assert.Equal("application/json", replay.Response.ContentType);
            Assert.Equal("/api/v1/profiles/1", replay.Response.Location);
            Assert.Equal("{\"id\":1}", Encoding.UTF8.GetString(replay.Response.Body.Span));
            Assert.Equal(1, await context.BusinessRows.CountAsync());
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NoContentResponseWithoutContentTypeIsCommittedAndReplayed()
    {
        await using var database = await Database.OpenAsync();
        var request = Request("first");
        var calls = 0;
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var response = await new EfCoreIdempotencyStore<TestContext>(context, new ManualClock())
                .ClaimAndExecuteAsync(request, TimeSpan.FromHours(1), _ =>
                {
                    calls++;
                    return Task.FromResult(new IdempotencyResponse(204, null, []));
                });
            Assert.Equal(204, response.StatusCode);
            Assert.Null(response.ContentType);
            Assert.Empty(response.Body.ToArray());
            await transaction.CommitAsync();
        }

        await using (var context = database.CreateContext())
        {
            var record = await context.Set<IdempotencyRecord>().SingleAsync();
            Assert.NotNull(record.CompletedAtUtcTicks);
            Assert.Null(record.ContentType);
            Assert.Empty(record.Body!);

            var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());
            var replay = await store.FindAsync(request);
            Assert.Equal(IdempotencyLookupKind.Replay, replay.Kind);
            Assert.Equal(204, replay.Response!.StatusCode);
            Assert.Null(replay.Response.ContentType);
            Assert.Null(replay.Response.Location);
            Assert.Empty(replay.Response.Body.ToArray());
            Assert.Equal(IdempotencyLookupKind.DifferentRequest, (await store.FindAsync(Request("changed"))).Kind);
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task IncompleteNoContentRecordStillCannotReplay()
    {
        await using var database = await Database.OpenAsync();
        var request = Request("incomplete");
        await SaveAsync(database, request);
        await using var context = database.CreateContext();
        var record = await context.Set<IdempotencyRecord>().SingleAsync();
        record.CompletedAtUtcTicks = null;
        record.StatusCode = 204;
        record.ContentType = null;
        record.Body = [];
        await context.SaveChangesAsync();

        var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(request));
    }

    [Fact]
    public async Task SameKeyWithDifferentRequestCannotReadStoredResponse()
    {
        await using var database = await Database.OpenAsync();
        await SaveAsync(database, Request("first"));
        await using var context = database.CreateContext();
        var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());

        var changedBody = await store.FindAsync(Request("changed"));
        Assert.Equal(IdempotencyLookupKind.DifferentRequest, changedBody.Kind);
        Assert.Null(changedBody.Response);
        Assert.Equal(IdempotencyLookupKind.Missing,
            (await store.FindAsync(Request("first", actor: "different-actor"))).Kind);
        Assert.Equal(IdempotencyLookupKind.Missing,
            (await store.FindAsync(Request("first", operation: "profiles.change"))).Kind);
    }

    [Fact]
    public async Task TwoCallersThatSawMissingCannotBothClaimTheSameKey()
    {
        await using var database = await Database.OpenAsync();
        var request = Request("first");
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var businessCalls = 0;

        async Task FirstAsync()
        {
            await using var context = database.CreateContext();
            var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());
            Assert.Equal(IdempotencyLookupKind.Missing, (await store.FindAsync(request)).Kind);
            firstRead.SetResult();
            await secondRead.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var transaction = await context.Database.BeginTransactionAsync();
            await store.ClaimAndExecuteAsync(request, TimeSpan.FromHours(1), _ =>
            {
                Interlocked.Increment(ref businessCalls);
                return Task.FromResult(Response());
            });
            await transaction.CommitAsync();
            firstCommitted.SetResult();
        }

        async Task SecondAsync()
        {
            await using var context = database.CreateContext();
            var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());
            Assert.Equal(IdempotencyLookupKind.Missing, (await store.FindAsync(request)).Kind);
            secondRead.SetResult();
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await firstCommitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<DbUpdateException>(() => store.ClaimAndExecuteAsync(request,
                TimeSpan.FromHours(1), _ =>
                {
                    Interlocked.Increment(ref businessCalls);
                    return Task.FromResult(Response());
                }));
        }

        await Task.WhenAll(FirstAsync(), SecondAsync());
        Assert.Equal(1, businessCalls);
        await using var check = database.CreateContext();
        Assert.Equal(IdempotencyLookupKind.Replay,
            (await new EfCoreIdempotencyStore<TestContext>(check, new ManualClock()).FindAsync(request)).Kind);
    }

    [Fact]
    public async Task RollbackAfterCompleteLeavesNeitherClaimNorBusinessWrite()
    {
        await using var database = await Database.OpenAsync();
        var request = Request("first");
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new EfCoreIdempotencyStore<TestContext>(context, new ManualClock())
                .ClaimAndExecuteAsync(request, TimeSpan.FromHours(1), _ =>
                {
                    context.BusinessRows.Add(new BusinessRow { Id = 1 });
                    return Task.FromResult(Response());
                });
            await transaction.RollbackAsync();
        }

        await using var check = database.CreateContext();
        Assert.Equal(IdempotencyLookupKind.Missing,
            (await new EfCoreIdempotencyStore<TestContext>(check, new ManualClock()).FindAsync(request)).Kind);
        Assert.Empty(await check.BusinessRows.ToListAsync());
    }

    [Fact]
    public async Task CancellationAfterClaimRollsBackWithTheCallerTransaction()
    {
        await using var database = await Database.OpenAsync();
        var request = Request("first");
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new EfCoreIdempotencyStore<TestContext>(context, new ManualClock()).ClaimAndExecuteAsync(request,
                    TimeSpan.FromHours(1), token =>
                    {
                        context.BusinessRows.Add(new BusinessRow { Id = 1 });
                        cancellation.Cancel();
                        return Task.FromResult(Response());
                    }, cancellation.Token));
            await transaction.RollbackAsync();
        }

        await using var check = database.CreateContext();
        Assert.Equal(IdempotencyLookupKind.Missing,
            (await new EfCoreIdempotencyStore<TestContext>(check, new ManualClock()).FindAsync(request)).Kind);
        Assert.Empty(await check.BusinessRows.ToListAsync());
    }

    [Fact]
    public async Task ExpiredRecordReplaysUntilExplicitPrune_ThenTheKeyCanBeReused()
    {
        await using var database = await Database.OpenAsync();
        var clock = new ManualClock();
        var request = Request("first");
        await SaveAsync(database, request, clock, TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(1));
        await using (var context = database.CreateContext())
        {
            var store = new EfCoreIdempotencyStore<TestContext>(context, clock);
            Assert.Equal(IdempotencyLookupKind.Replay, (await store.FindAsync(request)).Kind);
            Assert.Equal(1, await store.PruneExpiredAsync());
            Assert.Equal(IdempotencyLookupKind.Missing, (await store.FindAsync(request)).Kind);
        }
        await SaveAsync(database, request, clock);
        await using var check = database.CreateContext();
        Assert.Equal(IdempotencyLookupKind.Replay,
            (await new EfCoreIdempotencyStore<TestContext>(check, clock).FindAsync(request)).Kind);
    }

    [Fact]
    public async Task MissingTransactionAndOutOfRangeValuesAreRejectedBeforeClaim()
    {
        Assert.Throws<ArgumentException>(() => IdempotencyRequest.Create("profiles.create", "actor", "bad,key", "x"u8));
        Assert.Throws<ArgumentException>(() => IdempotencyRequest.Create("profiles.create", "actor", new string('x', 129), "x"u8));
        Assert.Throws<ArgumentException>(() => IdempotencyRequest.Create("profiles/create", "actor", "key", "x"u8));
        Assert.Throws<ArgumentException>(() => IdempotencyRequest.Create("profiles.create", "", "key", "x"u8));
        Assert.Throws<ArgumentException>(() => IdempotencyRequest.Create("profiles.create", new string('界', 342), "key", "x"u8));
        Assert.Throws<ArgumentOutOfRangeException>(() => IdempotencyRequest.Create("profiles.create", "actor", "key",
            new byte[IdempotencyRequest.MaximumRequestBytes + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyResponse(500, "application/json", "x"u8));
        Assert.Throws<ArgumentException>(() => new IdempotencyResponse(204, " ", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyResponse(200, "application/json",
            new byte[IdempotencyResponse.MaximumBodyBytes + 1]));

        await using var database = await Database.OpenAsync();
        await using var context = database.CreateContext();
        var store = new EfCoreIdempotencyStore<TestContext>(context, new ManualClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ClaimAndExecuteAsync(Request("first"),
            TimeSpan.FromHours(1), _ => Task.FromResult(Response())));
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ClaimAndExecuteAsync(Request("first"),
            TimeSpan.Zero, _ => Task.FromResult(Response())));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ClaimAndExecuteAsync(Request("first"),
            TimeSpan.FromDays(31), _ => Task.FromResult(Response())));
        Assert.Equal(IdempotencyLookupKind.Missing, (await store.FindAsync(Request("first"))).Kind);
    }

    private static IdempotencyRequest Request(string body, string actor = "issuer:subject", string operation = "profiles.create")
        => IdempotencyRequest.Create(operation, actor, "request-123", Encoding.UTF8.GetBytes(body));

    private static IdempotencyResponse Response() => new(201, "application/json", "{\"id\":1}"u8,
        "/api/v1/profiles/1");

    private static async Task SaveAsync(Database database, IdempotencyRequest request, ManualClock? clock = null,
        TimeSpan? retention = null)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new EfCoreIdempotencyStore<TestContext>(context, clock ?? new ManualClock())
            .ClaimAndExecuteAsync(request, retention ?? TimeSpan.FromHours(1), _ => Task.FromResult(Response()));
        await transaction.CommitAsync();
    }

    private sealed class BusinessRow { public int Id { get; set; } }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<BusinessRow> BusinessRows => Set<BusinessRow>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<BusinessRow>().HasKey(row => row.Id);
            model.AddHttpIdempotency();
        }
    }

    private sealed class Database(SqliteConnection anchor, DbContextOptions<TestContext> options) : IAsyncDisposable
    {
        public static async Task<Database> OpenAsync()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = "idempotency-" + Guid.NewGuid().ToString("N"),
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options = new DbContextOptionsBuilder<TestContext>().UseSqlite(connectionString).Options;
            await using (var context = new TestContext(options)) await context.Database.EnsureCreatedAsync();
            return new Database(anchor, options);
        }

        public TestContext CreateContext() => new(options);
        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = InitialTime;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
