using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Tasks.EFCore;

namespace MS.Microservice.Tasks.Messaging.Tests;

internal sealed class TaskMessagingDatabase(SqliteConnection connection, ServiceProvider services) : IAsyncDisposable
{
    public ServiceProvider Services { get; } = services;

    public static async Task<TaskMessagingDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        ServiceProvider? provider = null;
        try
        {
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<WorkflowContext>(options => options.UseSqlite(connection));
            services.AddEfCoreJobStore<WorkflowContext>();
            services.AddSelfManagedMessaging<WorkflowContext>(new(
                [MessageContract.For<ExportRequested>("report.export-requested")], []));
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowContext>().Database.EnsureCreatedAsync();
            return new(connection, provider);
        }
        catch
        {
            if (provider is not null) await provider.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ExportRequested>> ReadOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowContext>();
        // The provider keeps its persistence entities internal; inspect the actual SQLite rows without exposing them.
        var payloads = await context.Database.SqlQueryRaw<string>("SELECT \"Payload\" AS \"Value\" FROM \"Outbox\"").ToListAsync();
        return payloads.Select(payload => JsonSerializer.Deserialize<ExportRequested>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await connection.DisposeAsync();
    }
}

internal sealed class WorkflowContext(DbContextOptions<WorkflowContext> options) : DbContext(options)
{
    public DbSet<ExportRecord> Exports => Set<ExportRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ExportRecord>().HasKey(record => record.Id);
        model.AddTaskCenter();
        model.AddSelfManagedMessaging();
    }
}

internal sealed class ExportRecord
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
}

public sealed record ExportRequested(Guid Id, DateTimeOffset OccurredAtUtc, Guid JobId, Guid ExportId) : IIntegrationEvent;
internal sealed record ExportPayload(Guid ExportId, string Format);
