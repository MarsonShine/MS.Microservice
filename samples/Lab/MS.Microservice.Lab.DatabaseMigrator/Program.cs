using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Persistence.EFCore.DbContext;

var builder = Host.CreateApplicationBuilder(args);
var target = GetRequiredContext(args);
var seed = args.Contains("--seed-lab-users", StringComparer.Ordinal);
if (seed && target == "eventstore") throw new ArgumentException("Lab user seeding requires the activation target.");
if (seed)
{
    var database = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("ActivationConnection")).Database;
    if (database?.StartsWith("ms_lab_", StringComparison.Ordinal) != true)
        throw new ArgumentException("Lab user seeding is restricted to explicitly named ms_lab_ databases.");
}

if (target is "activation" or "all")
{
    await MigrateActivationAsync(builder.Configuration, seed);
}

if (target is "eventstore" or "all")
{
    await MigrateEventStoreAsync(builder.Configuration);
}

static string GetRequiredContext(string[] args)
{
    var contextIndex = Array.FindIndex(
        args,
        argument => string.Equals(argument, "--context", StringComparison.OrdinalIgnoreCase));
    if (contextIndex < 0 || contextIndex == args.Length - 1)
    {
        throw new ArgumentException(
            "A migration target is required. Use --context activation, --context eventstore, or --context all.");
    }

    var target = args[contextIndex + 1].ToLowerInvariant();
    return target is "activation" or "eventstore" or "all"
        ? target
        : throw new ArgumentException(
            $"Unsupported migration target '{target}'. Use activation, eventstore, or all.");
}

static async Task MigrateActivationAsync(IConfiguration configuration, bool seed)
{
    var connectionString = GetRequiredConnectionString(configuration, "ActivationConnection");
    var options = new DbContextOptionsBuilder<ActivationDbContext>()
        .UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__MigrationsHistory", ActivationDbContext.DEFAULT_SCHEMA))
        .Options;
    var settings = configuration
        .GetSection(MsPlatformDbContextSettings.SectionName)
        .Get<MsPlatformDbContextSettings>() ?? new MsPlatformDbContextSettings();

    await using var context = new ActivationDbContext(
        options,
        Options.Create(settings),
        new NoOpDomainEventDispatcher());
    await context.Database.MigrateAsync();
    if (seed)
        await MS.Microservice.Lab.Persistence.LabIdentitySeed.SeedAsync(context,
            configuration["LabBootstrap:OperatorPassword"] ?? "",
            configuration["LabBootstrap:ReaderPassword"] ?? "");
}

static async Task MigrateEventStoreAsync(IConfiguration configuration)
{
    var connectionString = GetRequiredConnectionString(configuration, "EventStoreConnection");
    var options = new DbContextOptionsBuilder<EventStoreDbContext>()
        .UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__MigrationsHistory", EventStoreDbContext.DefaultSchema))
        .Options;

    await using var context = new EventStoreDbContext(options);
    await context.Database.MigrateAsync();
}

static string GetRequiredConnectionString(IConfiguration configuration, string name)
{
    var connectionString = configuration.GetConnectionString(name);
    return !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
}
