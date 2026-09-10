using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using Weasel.Core.Migrations;
using global::Wolverine;
using global::Wolverine.Runtime;

namespace MS.Microservice.Reference.DatabaseMigrator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = MigrationCommand.Parse(args);
            await RunAsync(command);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Migration operation failed ({exception.GetType().Name}). Check the command, required configuration and database access.");
            return 1;
        }
    }

    public static async Task RunAsync(MigrationCommand command, CancellationToken cancellationToken = default)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__ReferenceDatabase");
        if ((command.Apply || command.NativeDiff) && string.IsNullOrWhiteSpace(connection))
            throw new ArgumentException("Applying migrations requires ConnectionStrings__ReferenceDatabase.");
        var brokerConnection = Environment.GetEnvironmentVariable("Messaging__RabbitMQ__ConnectionString");
        if (command.ProvisionBroker && string.IsNullOrWhiteSpace(brokerConnection))
            throw new ArgumentException("Provisioning requires Messaging__RabbitMQ__ConnectionString.");
        var broker = new RabbitMqOptions
        {
            ConnectionString = brokerConnection ?? "amqp://localhost",
            Exchange = Environment.GetEnvironmentVariable("Messaging__RabbitMQ__Exchange") ?? "ms.events",
            QueuePrefix = Environment.GetEnvironmentVariable("Messaging__RabbitMQ__QueuePrefix") ?? "ms.reference"
        };
        var output = Path.GetFullPath(command.OutputDirectory);
        Directory.CreateDirectory(output);
        var files = new List<string>();
        await using ReferenceDbContext database = command.Provider == "SelfManaged"
            ? new SelfManagedContextFactory().CreateDbContext([])
            : new WolverineContextFactory().CreateDbContext([]);
        IHost? nativeHost = null;
        try
        {
            if (command.Provider == "Wolverine")
            {
                var settings = new WolverineMessagingOptions
                {
                    ConnectionString = connection ?? "Host=localhost;Database=ms_reference_wolverine;Username=migrator",
                    BrokerConnectionString = broker.ConnectionString, Exchange = broker.Exchange, QueuePrefix = broker.QueuePrefix
                };
                nativeHost = Host.CreateDefaultBuilder()
                    .ConfigureLogging(logging => logging.ClearProviders().AddSimpleConsole())
                    .ConfigureServices(services =>
                    {
                        services.AddWolverineMessaging<WolverineReferenceDbContext>(ReferenceMessages.Topology(), settings);
                        services.AddReferenceRepositories<WolverineReferenceDbContext>();
                    })
                    .UseWolverine(options =>
                    {
                        WolverineMessagingExtensions.ConfigureWolverineMessaging<WolverineReferenceDbContext>(options, ReferenceMessages.Topology(), settings);
                        if (command.Apply) options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    }).Build();
                var runtime = nativeHost.Services.GetRequiredService<IWolverineRuntime>();
                var nativeDatabase = (IDatabase)runtime.Storage;
                var nativeScript = Path.Combine(output, command.NativeDiff ? "wolverine-messaging-diff.sql" : "wolverine-messaging-create.sql");
                if (command.NativeDiff)
                {
                    var migration = await nativeDatabase.CreateMigrationAsync(cancellationToken);
                    await using var writer = new StreamWriter(nativeScript, false, new UTF8Encoding(false));
                    migration.WriteAllUpdates(writer, nativeDatabase.Migrator, AutoCreate.CreateOrUpdate);
                }
                else await nativeDatabase.WriteCreationScriptToFileAsync(nativeScript, cancellationToken);
                files.Add(nativeScript);
                if (command.Apply) await runtime.Storage.Admin.MigrateAsync();
            }
            var script = database.GetService<IMigrator>().GenerateScript(Migration.InitialDatabase, null, MigrationsSqlGenerationOptions.Idempotent);
            var businessScript = Path.Combine(output, $"{command.Provider.ToLowerInvariant()}-business.sql");
            await File.WriteAllTextAsync(businessScript, script, new UTF8Encoding(false), cancellationToken);
            files.Add(businessScript);
            if (command.Apply) await database.Database.MigrateAsync(cancellationToken);
            if (command.ProvisionBroker)
                await new RabbitMqTopologyProvisioner(broker, ReferenceMessages.Topology(), broker.CreateConnectionFactory()).ProvisionAsync(cancellationToken);
            var manifest = new
            {
                formatVersion = 1, provider = command.Provider,
                targetMigration = database.Database.GetMigrations().LastOrDefault(),
                nativeScriptKind = command.Provider == "Wolverine" ? (command.NativeDiff ? "database-diff" : "creation; use --diff for an existing deployment") : null,
                files = files.Select(path => new { name = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) })
            };
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Console.WriteLine($"Exported {command.Provider} migration artifacts to {output}. Apply requested: {command.Apply}.");
        }
        finally { nativeHost?.Dispose(); }
    }
}

public sealed record MigrationCommand(string Provider, string OutputDirectory, bool Apply, bool ProvisionBroker, bool NativeDiff = false)
{
    private static string Value(string[] args, ref int index)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("A command option requires a value.");
        return args[index];
    }

    public static MigrationCommand Parse(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Messaging__Provider") ?? "SelfManaged";
        var output = "artifacts/migrations/reference";
        var apply = false;
        var provision = false;
        var diff = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--provider": provider = Value(args, ref index); break;
                case "--output": output = Value(args, ref index); break;
                case "--diff": diff = true; break;
                case "--apply": apply = true; break;
                case "--provision-broker": provision = true; break;
                default: throw new ArgumentException("Usage: --provider SelfManaged|Wolverine --output <directory> [--apply] [--provision-broker]");
            }
        }
        provider = provider.ToLowerInvariant() switch
        {
            "selfmanaged" => "SelfManaged", "wolverine" => "Wolverine", _ => throw new ArgumentException("Unknown messaging provider.")
        };
        if (diff && provider != "Wolverine") throw new ArgumentException("--diff applies only to native Wolverine schema.");
        return new(provider, output, apply, provision, diff);
    }
}
