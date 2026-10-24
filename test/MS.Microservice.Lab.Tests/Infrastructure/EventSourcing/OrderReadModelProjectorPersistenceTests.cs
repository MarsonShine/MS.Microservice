using System.Data.Common;
using System.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Orders;
using MS.Microservice.Infrastructure.EventSourcing.Repository;
using MS.Microservice.Infrastructure.EventSourcing.Serialization;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.EventSourcing;

public sealed class OrderReadModelProjectorPersistenceTests
{
    [Fact]
    public async Task SameNewOrderEventsPersistOneCombinedReadModel()
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        var order = Guid.NewGuid();
        await database.SeedAsync(
            new OrderCreated(order, "customer", "CNY"),
            new OrderItemAdded(order, "sku", 10m, 2));

        var checkpoint = await database.Projector.ProjectAsync();

        Assert.Equal(2, checkpoint);
        var model = Assert.Single(await database.Context.OrderReadModels.AsNoTracking().ToListAsync());
        Assert.Equal(order.ToString("D"), model.OrderId);
        Assert.Equal(2, model.ItemCount);
        Assert.Equal(20m, model.TotalAmount);
        Assert.Equal(2, model.Version);
        Assert.Equal(2, await database.Checkpoints.GetLastPositionAsync(OrderReadModelProjector.ProjectionName));
    }

    [Fact]
    public async Task BatchReaderLimitsTheDatabaseQueryAndPreservesOrderingFilteringAndBoundaries()
    {
        var probe = new EventReadProbe();
        await using var database = await ProjectionDatabase.CreateAsync(probe);
        var first = Guid.NewGuid();
        var other = Guid.NewGuid();
        await database.SeedAsync(new OrderCreated(first, "first", "CNY"));
        await database.SeedStreamAsync("other-stream", new OrderCreated(other, "other", "USD"));
        await database.SeedAsync(new OrderItemAdded(first, "sku", 10m, 1), new OrderConfirmed(first));
        IEventBatchReader reader = database.Events;

        var firstBatch = await reader.ReadBatchAsync<OrderEvent>(0, 2, OrderAggregate.StreamType);
        var lastBatch = await reader.ReadBatchAsync<OrderEvent>(firstBatch[^1].GlobalPosition, 2, OrderAggregate.StreamType);

        Assert.Equal(new long[] { 1, 3 }, firstBatch.Select(item => item.GlobalPosition));
        Assert.Equal(new long[] { 4 }, lastBatch.Select(item => item.GlobalPosition));
        Assert.IsType<OrderItemAdded>(firstBatch[1].Data);
        Assert.All(probe.Reads, read => Assert.Equal(2, read.Limit));
        Assert.Empty(await reader.ReadBatchAsync<OrderEvent>(4, 2, OrderAggregate.StreamType));
        Assert.Equal(new long[] { 2, 3 }, (await reader.ReadBatchAsync<OrderEvent>(1, 2)).Select(item => item.GlobalPosition));
        Assert.Equal(1, Assert.Single(await reader.ReadBatchAsync<OrderEvent>(-1, 1)).GlobalPosition);
        Assert.Empty(await reader.ReadBatchAsync<OrderEvent>(long.MaxValue));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reader.ReadBatchAsync<OrderEvent>(0, 0));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reader.ReadBatchAsync<OrderEvent>(0, 1001));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadBatchAsync<OrderEvent>(0, cancellationToken: canceled.Token));
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CatchUpPagesPreserveOrderTotalsAndReleaseOnlyProjectionOwnedTracking()
    {
        var probe = new EventReadProbe();
        await using var database = await ProjectionDatabase.CreateAsync(probe);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var events = new List<OrderEvent> { new OrderCreated(first, "first", "CNY"), new OrderCreated(second, "second", "USD") };
        for (var index = 0; index < 105; index++)
        {
            events.Add(new OrderItemAdded(first, "a", 2m, 1));
            events.Add(new OrderItemAdded(second, "b", 3m, 2));
        }
        await database.SeedAsync(events.ToArray());
        var external = new OrderReadModel { OrderId = Guid.NewGuid().ToString("D"), CustomerId = "external", Currency = "CNY" };
        database.Context.Add(external);
        await database.Context.SaveChangesAsync();
        var checkpoints = new CheckpointProbe(database.Checkpoints);
        var projector = new OrderReadModelProjector(database.Context, database.Events, checkpoints);

        Assert.Equal(212, await projector.ProjectAsync());

        Assert.Equal(new long[] { 100, 200, 212 }, checkpoints.Positions);
        Assert.Equal(3, probe.Reads.Count);
        Assert.All(probe.Reads, read =>
        {
            Assert.Equal(100, read.Limit);
            Assert.Same(external, Assert.Single(read.Tracked));
        });
        Assert.Same(external, Assert.Single(database.Context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(external).State);
        await using var check = database.OpenContext();
        var firstModel = await check.OrderReadModels.SingleAsync(model => model.OrderId == first.ToString("D"));
        var secondModel = await check.OrderReadModels.SingleAsync(model => model.OrderId == second.ToString("D"));
        Assert.Equal(105, firstModel.ItemCount);
        Assert.Equal(210m, firstModel.TotalAmount);
        Assert.Equal(106, firstModel.Version);
        Assert.Equal(210, secondModel.ItemCount);
        Assert.Equal(630m, secondModel.TotalAmount);
        Assert.Equal(106, secondModel.Version);
        Assert.Equal(212, await projector.ProjectAsync());
        Assert.Equal(3, checkpoints.Positions.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckpointFailureRollsBackModelAndPositionAndAllowsSameContextRetry(bool pretracked)
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        var order = Guid.NewGuid();
        await database.SeedAsync(new OrderCreated(order, "customer", "CNY"));
        OrderReadModel? tracked = null;
        ProjectionCheckpointRecord? trackedCheckpoint = null;
        if (pretracked)
        {
            Assert.Equal(1, await database.Projector.ProjectAsync());
            tracked = await database.Context.OrderReadModels.SingleAsync();
            trackedCheckpoint = await database.Context.ProjectionCheckpoints.SingleAsync();
        }
        await database.SeedAsync(new OrderItemAdded(order, "sku", 10m, 2));
        var checkpoint = new CheckpointProbe(database.Checkpoints) { FailNextStore = true };
        var projector = new OrderReadModelProjector(database.Context, database.Events, checkpoint);

        Assert.Same(checkpoint.Failure, await Assert.ThrowsAsync<IOException>(() => projector.ProjectAsync()));

        await using (var check = database.OpenContext())
        {
            Assert.Equal(pretracked ? 1 : 0, await new PostgresProjectionCheckpointStore(check)
                .GetLastPositionAsync(OrderReadModelProjector.ProjectionName));
            var model = await check.OrderReadModels.SingleOrDefaultAsync();
            if (pretracked)
            {
                Assert.NotNull(model);
                Assert.Equal(1, model.Version);
                Assert.Equal(0m, model.TotalAmount);
            }
            else Assert.Null(model);
        }
        if (pretracked)
        {
            Assert.Same(tracked, Assert.Single(database.Context.OrderReadModels.Local));
            Assert.Equal(1, tracked!.Version);
            Assert.Equal(0, tracked.ItemCount);
            Assert.Equal(0m, tracked.TotalAmount);
            Assert.Equal(EntityState.Unchanged, database.Context.Entry(tracked).State);
            Assert.Equal(1, database.Context.Entry(tracked).OriginalValues[nameof(OrderReadModel.Version)]);
            Assert.Equal(1, trackedCheckpoint!.LastGlobalPosition);
            Assert.Equal(EntityState.Unchanged, database.Context.Entry(trackedCheckpoint).State);
        }
        else Assert.Empty(database.Context.ChangeTracker.Entries());

        Assert.Equal(2, await projector.ProjectAsync());
        await using var resultContext = database.OpenContext();
        var result = Assert.Single(await resultContext.OrderReadModels.ToListAsync());
        Assert.Equal(2, result.Version);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(20m, result.TotalAmount);
        Assert.Equal(2, await database.Checkpoints.GetLastPositionAsync(OrderReadModelProjector.ProjectionName));
        Assert.False(database.Context.ChangeTracker.HasChanges());
        if (pretracked)
        {
            Assert.Same(tracked, Assert.Single(database.Context.OrderReadModels.Local));
            Assert.Equal(20m, tracked!.TotalAmount);
            Assert.Equal(2, trackedCheckpoint!.LastGlobalPosition);
        }
    }

    [Fact]
    public async Task LostCommitReplyDoesNotLetRestoredTrackedValuesCorruptTheNextPage()
    {
        var failure = new LostCommitReply();
        await using var database = await ProjectionDatabase.CreateAsync(failure);
        var order = Guid.NewGuid();
        await database.SeedAsync(new OrderCreated(order, "customer", "CNY"));
        Assert.Equal(1, await database.Projector.ProjectAsync());
        var tracked = await database.Context.OrderReadModels.SingleAsync();
        await database.SeedAsync(new OrderItemAdded(order, "first", 10m, 2));
        failure.Arm();

        Assert.Same(failure.Failure, await Assert.ThrowsAsync<IOException>(() => database.Projector.ProjectAsync()));

        await using (var committed = database.OpenContext())
        {
            Assert.Equal(20m, (await committed.OrderReadModels.SingleAsync()).TotalAmount);
            Assert.Equal(2, await new PostgresProjectionCheckpointStore(committed)
                .GetLastPositionAsync(OrderReadModelProjector.ProjectionName));
        }
        Assert.Same(tracked, Assert.Single(database.Context.OrderReadModels.Local));
        await database.SeedAsync(new OrderItemAdded(order, "next", 5m, 1));
        Assert.Same(tracked, Assert.Single(database.Context.OrderReadModels.Local));

        Assert.Equal(3, await database.Projector.ProjectAsync());

        Assert.Same(tracked, Assert.Single(database.Context.OrderReadModels.Local));
        Assert.Equal(25m, tracked.TotalAmount);
        Assert.Equal(3, tracked.ItemCount);
        Assert.Equal(3, tracked.Version);
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(tracked).State);
        await using var check = database.OpenContext();
        Assert.Equal(25m, (await check.OrderReadModels.SingleAsync()).TotalAmount);
    }

    [Fact]
    public async Task ProjectRejectsCallerChangesWithoutSavingOrClearingThem()
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        await database.SeedAsync(new OrderCreated(Guid.NewGuid(), "new", "CNY"));
        var external = new OrderReadModel { OrderId = Guid.NewGuid().ToString("D"), CustomerId = "original", Currency = "CNY" };
        database.Context.Add(external);
        await database.Context.SaveChangesAsync();
        external.CustomerId = "unsaved-caller-change";

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Projector.ProjectAsync());

        Assert.Equal(EntityState.Modified, database.Context.Entry(external).State);
        Assert.Equal("unsaved-caller-change", external.CustomerId);
        await using var check = database.OpenContext();
        Assert.Equal("original", (await check.OrderReadModels.SingleAsync()).CustomerId);
        Assert.Empty(await check.ProjectionCheckpoints.ToListAsync());
    }

    [Fact]
    public async Task ProjectRejectsCallerOwnedAndAmbientTransactions()
    {
        await using var database = await ProjectionDatabase.CreateAsync();
        await database.SeedAsync(new OrderCreated(Guid.NewGuid(), "new", "CNY"));
        await using (var transaction = await database.Context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Projector.ProjectAsync());
            Assert.Same(transaction, database.Context.Database.CurrentTransaction);
        }
        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Projector.ProjectAsync());
        Assert.Empty(await database.Context.OrderReadModels.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.ProjectionCheckpoints.AsNoTracking().ToListAsync());
    }

    private sealed class CheckpointProbe(IProjectionCheckpointStore inner) : IProjectionCheckpointStore
    {
        public List<long> Positions { get; } = [];
        public bool FailNextStore { get; set; }
        public IOException Failure { get; } = new("Checkpoint persistence reply failed before transaction commit.");
        public Task<long> GetLastPositionAsync(string projectionName, CancellationToken cancellationToken = default)
            => inner.GetLastPositionAsync(projectionName, cancellationToken);
        public async Task StoreAsync(string projectionName, long lastGlobalPosition, CancellationToken cancellationToken = default)
        {
            await inner.StoreAsync(projectionName, lastGlobalPosition, cancellationToken);
            Positions.Add(lastGlobalPosition);
            if (FailNextStore) { FailNextStore = false; throw Failure; }
        }
    }

    private sealed class LostCommitReply : DbTransactionInterceptor
    {
        private int _armed;
        public IOException Failure { get; } = new("Commit completed but its reply was lost.");
        public void Arm() => Volatile.Write(ref _armed, 1);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1) throw Failure;
            return Task.CompletedTask;
        }
    }

    private sealed class EventReadProbe : DbCommandInterceptor
    {
        public List<(int? Limit, object[] Tracked)> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FROM \"event_store\"", StringComparison.Ordinal))
            {
                var match = System.Text.RegularExpressions.Regex.Match(command.CommandText, @"\bLIMIT\s+([@:$]\w+|\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var value = match.Success ? match.Groups[1].Value : null;
                int? limit = value is null ? null : int.TryParse(value, out var literal) ? literal : Convert.ToInt32(command.Parameters[value].Value);
                Reads.Add((limit, eventData.Context!.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray()));
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ProjectionDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private long _position;
        private readonly Dictionary<Guid, int> _versions = [];
        public EventStoreDbContext Context { get; private set; } = null!;
        public SystemTextJsonEventSerializer Serializer { get; } = new(new EventTypeRegistry()
            .Register<OrderCreated>().Register<OrderItemAdded>().Register<OrderItemRemoved>()
            .Register<OrderConfirmed>().Register<OrderCancelled>());
        public PostgresEventStore Events => new(Context, Serializer);
        public PostgresProjectionCheckpointStore Checkpoints => new(Context);
        public OrderReadModelProjector Projector => new(Context, Events, Checkpoints);

        public static async Task<ProjectionDatabase> CreateAsync(params IInterceptor[] interceptors)
        {
            var database = new ProjectionDatabase();
            await database._connection.OpenAsync();
            database.Context = new(new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(database._connection).AddInterceptors(interceptors).Options);
            await database.Context.Database.EnsureCreatedAsync();
            return database;
        }

        public EventStoreDbContext OpenContext() => new(new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(_connection).Options);

        public Task SeedAsync(params OrderEvent[] events) => SeedStreamAsync(OrderAggregate.StreamType, events);

        public async Task SeedStreamAsync(string streamType, params OrderEvent[] events)
        {
            var records = new List<EventStoreRecord>();
            foreach (var item in events)
            {
                var serialized = Serializer.Serialize(item, new EventMetadata());
                var version = _versions.GetValueOrDefault(item.OrderId) + 1;
                _versions[item.OrderId] = version;
                records.Add(new()
                {
                    GlobalPosition = ++_position, EventId = Guid.NewGuid(),
                    StreamId = OrderAggregate.GetStreamId(item.OrderId), StreamType = streamType,
                    Version = version, EventType = serialized.EventType, Payload = serialized.Payload,
                    Metadata = serialized.Metadata, CreatedAt = DateTimeOffset.UtcNow
                });
            }
            Context.EventStore.AddRange(records);
            await Context.SaveChangesAsync();
            foreach (var record in records) Context.Entry(record).State = EntityState.Detached;
        }

        public async ValueTask DisposeAsync()
        {
            if (Context is not null) await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
