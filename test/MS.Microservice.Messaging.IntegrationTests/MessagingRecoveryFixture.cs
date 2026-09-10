using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Wolverine.Runtime;

namespace MS.Microservice.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class MessagingRecoveryCollection : ICollectionFixture<MessagingRecoveryFixture>
{
    public const string Name = "messaging-recovery";
}

public sealed class MessagingRecoveryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("messaging_contract").WithUsername("postgres").WithPassword("postgres").Build();
    private readonly RabbitMqContainer rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    public async Task InitializeAsync()
        => await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await rabbit.DisposeAsync();
        await postgres.DisposeAsync();
    }

    public async Task<IHost> CreateHostAsync(string provider)
    {
        // Every test receives a new database owned by the disposable fixture.
        var database = "ms_contract_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await command.ExecuteNonQueryAsync();
        }
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database }.ConnectionString;
        var topology = ReferenceMessages.Topology();
        var broker = new RabbitMqOptions
        {
            ConnectionString = rabbit.GetConnectionString(), Exchange = database, QueuePrefix = database
        };
        var builder = Host.CreateDefaultBuilder();
        if (provider == "SelfManaged")
        {
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseNpgsql(connection,
                    pg => pg.MigrationsHistoryTable("__MigrationsHistory", ReferenceDbContext.Schema)));
                services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
                services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(topology, options =>
                    options.PollInterval = TimeSpan.FromMilliseconds(100));
                services.AddRabbitMqTransport(broker);
            });
        }
        else
        {
            builder.UseWolverineMessaging<WolverineReferenceDbContext>(topology, new()
            {
                ConnectionString = connection, BrokerConnectionString = broker.ConnectionString,
                Exchange = broker.Exchange, QueuePrefix = broker.QueuePrefix
            });
            builder.ConfigureServices(services => services.AddReferenceRepositories<WolverineReferenceDbContext>());
        }
        var host = builder.Build();
        try
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            await context.Database.MigrateAsync();
            if (provider == "Wolverine")
            {
                // Explicit fixture DDL; production startup keeps AutoCreate.None.
                var databaseModel = (Weasel.Core.Migrations.IDatabase)host.Services.GetRequiredService<IWolverineRuntime>().Storage;
                var file = Path.GetTempFileName();
                try
                {
                    await databaseModel.WriteCreationScriptToFileAsync(file);
                    await context.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(file));
                }
                finally { File.Delete(file); }
            }
            await host.Services.GetRequiredService<RabbitMqTopologyProvisioner>().ProvisionAsync();
            return host;
        }
        catch { host.Dispose(); throw; }
    }

    public static async Task WaitForAuditAsync(IHost host, Guid profileId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            if (await context.Audit.AnyAsync(x => x.ProfileId == profileId, timeout.Token)) return;
            await Task.Delay(100, timeout.Token);
        }
    }
}
