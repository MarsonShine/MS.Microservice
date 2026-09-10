using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using MS.Microservice.Reference.Persistence;
using Xunit;

namespace MS.Microservice.Reference.Persistence.Tests;

public sealed class ProfilePersistenceTests
{
    [Fact]
    public async Task BusinessSaveAndPersistentEventLeadToOneAuditEffect()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseSqlite(connection));
        services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
        services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(ReferenceMessages.Topology());
        await using var provider = services.BuildServiceProvider();
        Guid id;
        SerializedMessage message;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            await context.Database.EnsureCreatedAsync();
            var result = await scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                new("https://issuer.example", "subject", "中文", ["reader"]), new("https://issuer.example", "admin"));
            Assert.True(result.IsRight);
            id = result.Right.Id;
            await using var command = connection.CreateCommand();
            command.CommandText = "select Payload from Outbox";
            var payload = (string)(await command.ExecuteScalarAsync())!;
            var data = System.Text.Json.JsonSerializer.Deserialize<UserProfileChangedV1>(payload,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            message = ReferenceMessages.Topology().Registry.Serialize(data);
        }
        var receiver = provider.GetRequiredService<IMessageReceiver>();
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(message, "profile-audit", default));
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(message, "profile-audit", default));
        var sameEffect = (UserProfileChangedV1)ReferenceMessages.Topology().Registry.Deserialize(message);
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(
            ReferenceMessages.Topology().Registry.Serialize(sameEffect with { Id = Guid.NewGuid() }), "profile-audit", default));
        await using var check = provider.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
        Assert.Equal("中文", (await database.Profiles.SingleAsync()).DisplayName);
        Assert.Equal(id, (await database.Audit.SingleAsync()).ProfileId);
    }

    [Fact]
    public async Task RetainedAndRemovedRolesPersistWithoutTrackingConflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", ["reader"], DateTimeOffset.UtcNow);
        context.Add(profile);
        await context.SaveChangesAsync();
        profile.Change("name", ["reader", "editor"], DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
        profile.Change("name", ["editor"], DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var saved = await context.Profiles.Include(x => x.Roles).SingleAsync();
        Assert.Equal("editor", Assert.Single(saved.Roles).Name);
        Assert.Equal(3, saved.Version);
    }

    [Fact]
    public async Task DatabaseEnforcesIdentityAndOptimisticVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var first = Context(connection);
        await first.Database.EnsureCreatedAsync();
        first.Add(UserProfile.Create("https://issuer.example", "subject", "name", [], DateTimeOffset.UtcNow));
        await first.SaveChangesAsync();
        await using var second = Context(connection);
        var stale = await second.Profiles.Include(x => x.Roles).SingleAsync();
        var current = await first.Profiles.SingleAsync();
        current.Change("first", [], DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        stale.Change("second", [], DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.ChangeTracker.Clear();
        second.Add(UserProfile.Create("https://issuer.example", "subject", "duplicate", [], DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public void ProvidersHaveDistinctModelsWithoutSharingMessageTables()
    {
        using var self = new SelfManagedContextFactory().CreateDbContext([]);
        using var native = new WolverineContextFactory().CreateDbContext([]);
        Assert.False(self.Database.HasPendingModelChanges());
        Assert.False(native.Database.HasPendingModelChanges());
        Assert.Contains(self.Model.GetEntityTypes(), type => type.GetTableName() == "Outbox");
        Assert.DoesNotContain(native.Model.GetEntityTypes(), type => type.GetTableName() == "Outbox");
        Assert.Contains(native.Model.GetEntityTypes(), type => type.GetTableName()!.Contains("outgoing", StringComparison.OrdinalIgnoreCase));
    }

    private static SelfManagedReferenceDbContext Context(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<SelfManagedReferenceDbContext>().UseSqlite(connection).Options);
}
