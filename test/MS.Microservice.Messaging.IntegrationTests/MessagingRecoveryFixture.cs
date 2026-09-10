using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Messaging.FaultWorker;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Reference.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using global::Wolverine.Runtime;

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
    private readonly RabbitMqContainer rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("test").WithPassword("test").WithPortBinding(15672, true).Build();

    public async Task InitializeAsync() => await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
    public async Task DisposeAsync() { await rabbit.DisposeAsync(); await postgres.DisposeAsync(); }
    public Task StopBrokerAsync() => rabbit.StopAsync();
    public Task StartBrokerAsync() => rabbit.StartAsync();
    public Task StopDatabaseAsync() => postgres.StopAsync();
    public Task StartDatabaseAsync() => postgres.StartAsync();

    public async Task<FaultEnvironment> CreateEnvironmentAsync(string provider)
    {
        var database = "ms_contract_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await command.ExecuteNonQueryAsync();
        }
        var environment = new FaultEnvironment(provider,
            new Npgsql.NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database }.ConnectionString,
            rabbit.GetConnectionString(), database);
        using var host = FaultHost.Build(environment);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
        await context.Database.MigrateAsync();
        if (provider == "Wolverine")
        {
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
        return environment;
    }

    public async Task<IHost> CreateHostAsync(string provider) => FaultHost.Build(await CreateEnvironmentAsync(provider));

    public static async Task WaitForAuditAsync(IHost host, Guid profileId)
        => await UntilAsync(async token =>
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Audit.AnyAsync(x => x.ProfileId == profileId, token);
        });

    public async Task WaitForDrainedAsync(FaultEnvironment environment, IHost probe)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("test:test")));
        var queueUrl = $"http://{rabbit.Hostname}:{rabbit.GetMappedPublicPort(15672)}/api/queues/%2F/{environment.Broker.Queue("profile-audit")}";
        await UntilAsync(async token =>
        {
            HttpResponseMessage response;
            try { response = await client.GetAsync(queueUrl, token); }
            catch (HttpRequestException) { return false; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            using var responseLifetime = response;
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("messages", out var messages) || messages.GetInt32() != 0
                || !json.RootElement.TryGetProperty("consumers", out var consumers) || consumers.GetInt32() < 1) return false;
            if (environment.Provider == "Wolverine")
            {
                var counts = await probe.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.FetchCountsAsync();
                return counts.Incoming == 0 && counts.Scheduled == 0 && counts.Outgoing == 0 && counts.DeadLetter == 0;
            }
            await using var connection = new Npgsql.NpgsqlConnection(environment.ConnectionString);
            await connection.OpenAsync(token);
            await using var command = new Npgsql.NpgsqlCommand("""
                SELECT (SELECT COUNT(*) FROM messaging."Outbox" WHERE "State" <> 2)
                     + (SELECT COUNT(*) FROM messaging."Inbox" WHERE "State" <> 1)
                """, connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token)) == 0;
        });
    }

    public static async Task UntilAsync(Func<CancellationToken, Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (await predicate(timeout.Token)) return;
            await Task.Delay(200, timeout.Token);
        }
    }
}
