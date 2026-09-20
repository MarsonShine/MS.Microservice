using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using Wolverine;

namespace MS.Microservice.Lab.Hosting;

public static class LabMessaging
{
    public static void Configure(WebApplicationBuilder builder, Action<WolverineOptions> configureLocal)
    {
        if (!builder.Configuration.GetValue("LabMessaging:Enabled", false))
        {
            builder.Host.UseWolverine(configureLocal);
            return;
        }
        var connection = builder.Configuration.GetConnectionString("LabMessagingDatabase");
        if (string.IsNullOrWhiteSpace(connection))
            throw new ArgumentException("ConnectionStrings:LabMessagingDatabase is required.");
        var legacy = builder.Configuration.GetConnectionString("ActivationConnection");
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var old = new Npgsql.NpgsqlConnectionStringBuilder(legacy);
            var current = new Npgsql.NpgsqlConnectionStringBuilder(connection);
            if (old.Host == current.Host && old.Port == current.Port && old.Database == current.Database)
                throw new ArgumentException("Lab messaging must use a database separate from legacy exercises.");
        }
        var topology = ReferenceMessages.Topology();
        var broker = builder.Configuration.GetSection("Messaging:RabbitMQ").Get<RabbitMqOptions>() ?? new();
        broker.Validate();
        var provider = builder.Configuration["Messaging:Provider"] ?? "SelfManaged";
        if (provider.Equals("SelfManaged", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseNpgsql(connection,
                postgres => postgres.MigrationsHistoryTable("__MigrationsHistory", ReferenceDbContext.Schema)));
            builder.Services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
            builder.Services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(topology,
                options => builder.Configuration.GetSection("Messaging:SelfManaged").Bind(options));
            builder.Services.AddRabbitMqTransport(broker);
            builder.Host.UseWolverine(configureLocal);
        }
        else if (provider.Equals("Wolverine", StringComparison.OrdinalIgnoreCase))
        {
            var settings = new WolverineMessagingOptions
            {
                ConnectionString = connection, BrokerConnectionString = broker.ConnectionString,
                Exchange = broker.Exchange, QueuePrefix = broker.QueuePrefix
            };
            builder.Services.AddWolverineMessaging<WolverineReferenceDbContext>(topology, settings);
            builder.Services.AddReferenceRepositories<WolverineReferenceDbContext>();
            builder.Host.UseWolverine(options =>
            {
                configureLocal(options);
                WolverineMessagingExtensions.ConfigureWolverineMessaging<WolverineReferenceDbContext>(options, topology, settings,
                    [WolverineMessageRegistration<WolverineReferenceDbContext>.For<UserProfileChangedV1>()]);
            });
        }
        else throw new ArgumentException("Messaging:Provider must be SelfManaged or Wolverine.");
    }

    public static void Map(WebApplication app)
    {
        if (!app.Configuration.GetValue("LabMessaging:Enabled", false)) return;
        var lesson = app.MapGroup("/lab/messaging").RequireAuthorization();
        lesson.MapPost("/profiles", async (CreateProfile request, ProfileService service, HttpContext http,
            CancellationToken token) =>
        {
            var actor = new AuditActor(http.User.FindFirst("iss")?.Value ?? "",
                http.User.FindFirst("sub")?.Value ?? "");
            var result = await service.CreateAsync(request, actor, token);
            return result.Match<IResult>(error => Results.Problem(statusCode: error.Code == "conflict" ? 409 : 400,
                title: error.Message), profile => Results.Created($"/lab/messaging/profiles/{profile.Id}", profile));
        });
        lesson.MapGet("/audit", async (IProfileAuditRepository repository, Guid? profileId, CancellationToken token) =>
            Results.Ok(await repository.ListAsync(profileId, 50, token)));
        lesson.MapGet("/failures", async (IFailedMessageOperations failures, CancellationToken token) =>
            Results.Ok(await failures.ListAsync(100, token))).RequireAuthorization("LabMessagingOperations");
        lesson.MapPost("/failures/{failureId}/replay", async (string failureId, IFailedMessageOperations failures,
            CancellationToken token) => await failures.ReplayAsync(failureId, token) switch
            {
                ReplayResult.Accepted => Results.Accepted(),
                ReplayResult.NotFound => Results.NotFound(),
                _ => Results.Conflict()
            }).RequireAuthorization("LabMessagingOperations");
    }
}
