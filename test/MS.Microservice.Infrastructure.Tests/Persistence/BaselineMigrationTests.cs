using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Persistence.EFCore.DbContext;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Persistence;

public sealed class BaselineMigrationTests
{
    [Fact]
    public void ActivationBaseline_IsDiscoverableAndGeneratesIdempotentPostgresScript()
    {
        using var context = CreateActivationContext();

        var migrations = context.Database.GetMigrations().ToArray();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: Migration.InitialDatabase,
            toMigration: migrations.Last(),
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Equal(2, migrations.Length);
        Assert.Contains(migrations, migration => migration.EndsWith("_BaselineIdentityAndLog", StringComparison.Ordinal));
        Assert.Contains("fz_platform_activation", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE", script, StringComparison.Ordinal);
        Assert.Contains("\"Users\"", script, StringComparison.Ordinal);
        Assert.Contains("\"Logs\"", script, StringComparison.Ordinal);
        Assert.Contains("\"OutboxMessages\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EventSourcingBaseline_IsDiscoverableAndGeneratesIdempotentPostgresScript()
    {
        using var context = CreateEventStoreContext();

        var migrations = context.Database.GetMigrations().ToArray();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var migration = migrationsAssembly.CreateMigration(
            migrationsAssembly.Migrations.Single().Value,
            context.Database.ProviderName!);
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: Migration.InitialDatabase,
            toMigration: migrations.Single(),
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Single(migrations);
        Assert.NotEmpty(migration.UpOperations);
        Assert.EndsWith("_BaselineEventSourcing", migrations[0], StringComparison.Ordinal);
        Assert.Contains("event_sourcing", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE", script, StringComparison.Ordinal);
        Assert.Contains("event_store", script, StringComparison.Ordinal);
        Assert.Contains("snapshots", script, StringComparison.Ordinal);
        Assert.Contains("projection_checkpoint", script, StringComparison.Ordinal);
    }

    private static ActivationDbContext CreateActivationContext()
    {
        var options = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseNpgsql(DesignConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__MigrationsHistory",
                    ActivationDbContext.DEFAULT_SCHEMA))
            .Options;
        return new ActivationDbContext(
            options,
            Options.Create(new MsPlatformDbContextSettings()),
            new NoOpDomainEventDispatcher());
    }

    private static EventStoreDbContext CreateEventStoreContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseNpgsql(DesignConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__MigrationsHistory",
                    EventStoreDbContext.DefaultSchema))
            .Options;
        return new EventStoreDbContext(options);
    }

    private const string DesignConnectionString =
        "Host=localhost;Database=migration_script_test;Username=postgres;Password=postgres";
}
