using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Core.Security.SensitiveData;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class AutomaticAuditingTests
{
    [Fact]
    public void SensitiveKeysCannotBypassFieldProtectionThroughResourceIdentity()
    {
        var options = new DbContextOptionsBuilder<SensitiveKeyContext>().UseSqlite("Data Source=:memory:").Options;
        using var context = new SensitiveKeyContext(options, new AuditOperationContext());
        context.Add(new SensitiveKeyEntity { Id = "13800138000", Name = "example" });
        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("sensitive keys", error.Message);
    }
    [Fact]
    public async Task ProtectedAuditComparesRawValuesAndPersistsOnlyMasksOrChangeFacts()
    {
        await using var fixture = await Fixture.Create(); await using var context = fixture.Context();
        using var operation = fixture.Operation.Begin("scope", "actor", "protected.change");
        var order = await context.Orders.SingleAsync();
        order.Phone = "13899118000"; order.InternalNote = "never-store-in-audit";
        await context.SaveChangesAsync();
        var record = await context.ForAuditScope("scope").SingleAsync();
        Assert.DoesNotContain("13800138000", record.ChangesJson); Assert.DoesNotContain(order.Phone, record.ChangesJson);
        Assert.DoesNotContain(order.InternalNote, record.ChangesJson);
        using var json = JsonDocument.Parse(record.ChangesJson);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        var phone = json.RootElement[0]; Assert.Equal("Masked", phone.GetProperty("protection").GetString());
        Assert.Equal("138****8000", phone.GetProperty("before").GetString());
        Assert.Equal(phone.GetProperty("before").GetString(), phone.GetProperty("after").GetString());
        var secret = json.RootElement[1]; Assert.Equal("ChangedOnly", secret.GetProperty("protection").GetString());
        Assert.False(secret.TryGetProperty("before", out _)); Assert.False(secret.TryGetProperty("after", out _));
        Assert.Equal("13899118000", (await context.Orders.AsNoTracking().SingleAsync()).Phone);
    }
    [Fact]
    public async Task SynchronousSavesCaptureCreationUpdateAndDeletionWithAcceptedOriginals()
    {
        await using var fixture = await Fixture.Create();
        using var context = fixture.Context();
        var suppliedId = Guid.NewGuid();
        using var operation = fixture.Operation.Begin("scope", "actor", "lifecycle", suppliedId);
        var row = new AuditStock { Id = Guid.NewGuid(), Available = 10 };
        context.Stock.Add(row); context.SaveChanges();
        row.Available = 9; context.SaveChanges();
        context.Stock.Remove(row); context.SaveChanges();
        var records = context.ForAuditScope("scope").OrderBy(value => value.SaveSequence).ToArray();
        Assert.All(records, value => { Assert.Equal(suppliedId, value.OperationId); Assert.Equal(7, value.Id.Version); });
        Assert.Equal(new[] { AuditChangeKind.Created, AuditChangeKind.Updated, AuditChangeKind.Deleted }, records.Select(value => value.Kind));
        using var created = JsonDocument.Parse(records[0].ChangesJson);
        using var updated = JsonDocument.Parse(records[1].ChangesJson);
        using var deleted = JsonDocument.Parse(records[2].ChangesJson);
        Assert.False(created.RootElement[0].GetProperty("beforeKnown").GetBoolean());
        Assert.Equal(10, updated.RootElement[0].GetProperty("before").GetInt32());
        Assert.Equal(9, deleted.RootElement[0].GetProperty("before").GetInt32());
        Assert.False(deleted.RootElement[0].TryGetProperty("after", out _));
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    [Fact]
    public async Task MultiTableChangesShareOperationAndRollbackWithTheirBusinessTransaction()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context();
        var order = await context.Orders.SingleAsync();
        var stock = await context.Stock.SingleAsync();
        using var operation = fixture.Operation.Begin("tenant-a", "alice", "order.approve");
        var id = fixture.Operation.Current!.Id;
        Assert.Equal(7, id.Version);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            using (fixture.Operation.Begin("tenant-a", "alice", "nested"))
            {
                order.Status = "Approved"; order.Secret = "never-serialize-this"; stock.Available -= 2;
                await context.SaveChangesAsync();
            }
            var records = await context.ForAuditScope("tenant-a").OrderBy(record => record.ResourceType).ToListAsync();
            Assert.Equal(2, records.Count);
            Assert.All(records, record => { Assert.Equal(id, record.OperationId); Assert.Equal(1, record.SaveSequence); Assert.Equal("order.approve", record.Action); });
            Assert.DoesNotContain("never-serialize", string.Join(',', records.Select(record => record.ChangesJson)));
            using var changes = JsonDocument.Parse(records[0].ChangesJson);
            Assert.Equal("Pending", changes.RootElement[0].GetProperty("before").GetString());
            Assert.True(changes.RootElement[0].GetProperty("beforeKnown").GetBoolean());
            order.Status = "Packed";
            await context.SaveChangesAsync();
            Assert.Equal(3, await context.ForAuditScope("tenant-a").CountAsync());
            Assert.Equal(2, await context.ForAuditScope("tenant-a").MaxAsync(record => record.SaveSequence));
            await transaction.RollbackAsync();
        }
        await using var check = fixture.Context();
        Assert.Equal("Pending", (await check.Orders.SingleAsync()).Status);
        Assert.Empty(await check.ForAuditScope("tenant-a").ToListAsync());
    }

    [Fact]
    public async Task AutomaticTransactionPersistsChangesOnceAndDoesNotAuditUnselectedFields()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context();
        using var operation = fixture.Operation.Begin("Owner", "alice", "order.edit");
        var order = await context.Orders.SingleAsync();
        order.Status = "Approved";
        await context.SaveChangesAsync();
        await context.SaveChangesAsync();
        order.Secret = "private";
        await context.SaveChangesAsync();
        Assert.Single(await context.ForAuditScope("Owner").ToListAsync());
        Assert.Empty(await context.ForAuditScope("owner").ToListAsync());
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    [Fact]
    public async Task FailedSaveCallbackRestoresDetectionAndDoesNotLeaveDuplicateAuditRowsOnRetry()
    {
        var failure = new ThrowingInterceptor();
        await using var fixture = await Fixture.Create(failure);
        await using var context = fixture.Context();
        var order = await context.Orders.SingleAsync();
        using var operation = fixture.Operation.Begin("scope", "actor", "edit");
        order.Status = "Approved"; failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => context.SaveChangesAsync());
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.DoesNotContain(context.ChangeTracker.Entries<AuditRecord>(), entry => entry.State == EntityState.Added);
        failure.Fail = false;
        await context.SaveChangesAsync();
        Assert.Single(await context.ForAuditScope("scope").ToListAsync());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackAuditAndBusinessAndCanBeRetried()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context();
        var order = await context.Orders.SingleAsync();
        using var operation = fixture.Operation.Begin("scope", "actor", "edit");
        order.Status = "Approved";
        var duplicate = new AuditOrder { Id = Guid.NewGuid(), Code = order.Code };
        context.Orders.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await using (var check = fixture.Context())
        {
            Assert.Empty(await check.ForAuditScope("scope").ToListAsync());
            Assert.Equal("Pending", (await check.Orders.SingleAsync()).Status);
        }
        context.Entry(duplicate).State = EntityState.Detached;
        await context.SaveChangesAsync();
        Assert.Single(await context.ForAuditScope("scope").ToListAsync());
    }

    [Fact]
    public async Task AttachedUpdatesDeclareUnknownOriginalsWithoutAnExtraRead()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context();
        var order = await context.Orders.SingleAsync();
        context.ChangeTracker.Clear();
        order.Status = "Approved";
        context.Attach(order); context.Entry(order).Property(value => value.Status).IsModified = true;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        using var operation = fixture.Operation.Begin("scope", "actor", "edit");
        await context.SaveChangesAsync();
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        var record = await context.ForAuditScope("scope").SingleAsync();
        using var json = JsonDocument.Parse(record.ChangesJson);
        Assert.False(json.RootElement[0].GetProperty("beforeKnown").GetBoolean());
        Assert.False(json.RootElement[0].TryGetProperty("before", out _));
    }

    [Fact]
    public async Task BudgetMissingIdentityAndCancellationFailBeforePublishingAndRestoreState()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context(new() { MaximumRecordBytes = 128 });
        var order = await context.Orders.SingleAsync();
        order.Status = "Approved";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        using var operation = fixture.Operation.Begin("scope", "actor", "edit");
        order.Status = new string('x', 256);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
        order.Status = "Approved";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(new CancellationToken(true)));
        await context.SaveChangesAsync();
        Assert.Single(await context.ForAuditScope("scope").ToListAsync());
    }

    [Fact]
    public async Task TemporaryKeysAndNonAcceptingSavesAreExplicitlyRejected()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Context();
        using var operation = fixture.Operation.Begin("scope", "actor", "create");
        var generated = new GeneratedRow { Name = "new" }; context.Generated.Add(generated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.Entry(generated).State = EntityState.Detached;
        (await context.Orders.SingleAsync()).Status = "Approved";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(false));
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
        await context.SaveChangesAsync();
        Assert.Single(await context.ForAuditScope("scope").ToListAsync());
    }

    private sealed class SensitiveKeyEntity
    {
        [Sensitive(SensitiveKind.Phone)] public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
    private sealed class SensitiveKeyContext(DbContextOptions<SensitiveKeyContext> options, AuditOperationContext operation)
        : AuditedDbContext(options, operation)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { model.AddAuditRecords(); model.Entity<SensitiveKeyEntity>().HasKey(value => value.Id); model.Entity<SensitiveKeyEntity>().AuditChanges("sample", value => value.Name); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly IInterceptor? _interceptor;
        internal AuditOperationContext Operation { get; } = new();
        internal Guid OrderId { get; } = Guid.NewGuid();
        private Fixture(IInterceptor? interceptor) => _interceptor = interceptor;
        internal static async Task<Fixture> Create(IInterceptor? interceptor = null)
        {
            var fixture = new Fixture(interceptor); await fixture._connection.OpenAsync();
            await using var context = fixture.Context(); context.AuditingEnabled = false;
            await context.Database.EnsureCreatedAsync();
            context.Orders.Add(new() { Id = fixture.OrderId, Code = "order" });
            context.Stock.Add(new() { Id = Guid.NewGuid(), Available = 100 });
            await context.SaveChangesAsync(); return fixture;
        }
        internal AuditTestContext Context(AuditCollectionOptions? limits = null)
        {
            var builder = new DbContextOptionsBuilder<AuditTestContext>().UseSqlite(_connection);
            if (_interceptor is not null) builder.AddInterceptors(_interceptor);
            return new(builder.Options, Operation, limits);
        }
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
    private sealed class ThrowingInterceptor : SaveChangesInterceptor
    {
        internal bool Fail;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => Fail ? throw new IOException("save callback failed") : ValueTask.FromResult(result);
    }
    private sealed class AuditTestContext(DbContextOptions<AuditTestContext> options, AuditOperationContext operation, AuditCollectionOptions? limits)
        : AuditedDbContext(options, operation, limits)
    {
        internal DbSet<AuditOrder> Orders => Set<AuditOrder>();
        internal DbSet<AuditStock> Stock => Set<AuditStock>();
        internal DbSet<GeneratedRow> Generated => Set<GeneratedRow>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.AddAuditRecords();
            model.Entity<AuditOrder>().HasKey(value => value.Id);
            model.Entity<AuditOrder>().Property(value => value.Id).ValueGeneratedNever();
            model.Entity<AuditOrder>().HasIndex(value => value.Code).IsUnique();
            model.Entity<AuditOrder>().AuditChanges("order", value => new { value.Status, value.Phone, value.InternalNote });
            model.Entity<AuditStock>().HasKey(value => value.Id);
            model.Entity<AuditStock>().Property(value => value.Id).ValueGeneratedNever();
            model.Entity<AuditStock>().AuditChanges("stock", value => value.Available);
            model.Entity<GeneratedRow>().HasKey(value => value.Id);
            model.Entity<GeneratedRow>().AuditChanges("generated", value => value.Name);
        }
    }
    private sealed class AuditOrder
    {
        public Guid Id { get; set; } public string Code { get; set; } = ""; public string Status { get; set; } = "Pending"; public string Secret { get; set; } = "";
        [Sensitive(SensitiveKind.Phone)] public string? Phone { get; set; } = "13800138000";
        [Sensitive(SensitiveKind.Secret)] public string? InternalNote { get; set; }
    }
    private sealed class AuditStock { public Guid Id { get; set; } public int Available { get; set; } }
    private sealed class GeneratedRow { public int Id { get; set; } public string Name { get; set; } = ""; }
}
