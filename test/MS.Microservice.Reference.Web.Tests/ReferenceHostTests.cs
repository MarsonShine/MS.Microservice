using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using RabbitMQ.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.AspNetCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using MS.Microservice.Reference.Persistence;
using MS.Microservice.Reference.Web;
using Xunit;

namespace MS.Microservice.Reference.Web.Tests;

public sealed class ReferenceHostTests
{
    [Fact]
    public async Task FormalHostRequiresIdentityAndContainsNoLabOrLocalLoginRoutes()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var live = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await live.Content.ReadAsStringAsync());
        using var anonymous = await fixture.Client.GetAsync("/api/v1/profiles");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        fixture.Authenticate("profiles.manage");
        foreach (var path in new[] { "/api/demo", "/api/v1/Account/login", "/api/orders", "/api/image" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProfileCrudAndOperationsPermissionsUseTheActualHostPipeline()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "external-subject", "first", ["reader"]);
        using var created = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<ProfileView>())!;
        using var changed = await fixture.Client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new ChangeProfile("second", ["editor"], 1));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(2, (await changed.Content.ReadFromJsonAsync<ProfileView>())!.Version);
        using var stale = await fixture.Client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new ChangeProfile("third", [], 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertProblemCodeAsync(stale, "conflict");
        using var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertProblemCodeAsync(duplicate, "conflict");
        using var denied = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        fixture.Authenticate("messaging.manage");
        using var allowed = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task InvalidQueryValuesAreRejectedBeforeRepositoryMethodsRun()
    {
        var profiles = Substitute.For<IProfileRepository>();
        var audits = Substitute.For<IProfileAuditRepository>();
        var failures = Substitute.For<IFailedMessageOperations>();
        await using var fixture = await Fixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.RemoveAll<IProfileAuditRepository>();
            services.RemoveAll<IFailedMessageOperations>();
            services.AddSingleton(profiles);
            services.AddSingleton(audits);
            services.AddSingleton(failures);
        });
        fixture.Authenticate("profiles.manage");
        foreach (var path in new[]
        {
            "/api/v1/profiles?skip=-1", "/api/v1/profiles?take=0", "/api/v1/profiles?take=201",
            "/api/v1/profiles?take=invalid", "/api/v1/audit?take=0", "/api/v1/audit?take=201"
        })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} returned {(int)response.StatusCode}.");
        }

        fixture.Authenticate("messaging.manage");
        foreach (var path in new[] { "/api/operations/messages/failures?limit=0", "/api/operations/messages/failures?limit=1001" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} returned {(int)response.StatusCode}.");
        }

        _ = profiles.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
        _ = audits.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
        _ = failures.DidNotReceiveWithAnyArgs().ListAsync(default, default);
    }

    [Fact]
    public async Task QueryDefaultsNormalValuesAndInclusiveLimitsReachRepositories()
    {
        var profiles = Substitute.For<IProfileRepository>();
        profiles.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserProfile>>([]));
        var audits = Substitute.For<IProfileAuditRepository>();
        audits.ListAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProfileAuditEntry>>([]));
        var failures = Substitute.For<IFailedMessageOperations>();
        failures.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FailedMessage>>([]));
        await using var fixture = await Fixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.RemoveAll<IProfileAuditRepository>();
            services.RemoveAll<IFailedMessageOperations>();
            services.AddSingleton(profiles);
            services.AddSingleton(audits);
            services.AddSingleton(failures);
        });
        fixture.Authenticate("profiles.manage");
        using var profileDefault = await fixture.Client.GetAsync("/api/v1/profiles");
        using var profileNormal = await fixture.Client.GetAsync("/api/v1/profiles?skip=7&take=25");
        using var profileMax = await fixture.Client.GetAsync("/api/v1/profiles?skip=2147483647&take=200");
        using var auditDefault = await fixture.Client.GetAsync("/api/v1/audit");
        var profileId = Guid.NewGuid();
        using var auditMax = await fixture.Client.GetAsync($"/api/v1/audit?profileId={profileId}&take=200");
        Assert.All(new[] { profileDefault, profileNormal, profileMax, auditDefault, auditMax },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        _ = profiles.Received(1).ListAsync(0, 50, Arg.Any<CancellationToken>());
        _ = profiles.Received(1).ListAsync(7, 25, Arg.Any<CancellationToken>());
        _ = profiles.Received(1).ListAsync(int.MaxValue, 200, Arg.Any<CancellationToken>());
        _ = audits.Received(1).ListAsync(null, 50, Arg.Any<CancellationToken>());
        _ = audits.Received(1).ListAsync(profileId, 200, Arg.Any<CancellationToken>());

        fixture.Authenticate("messaging.manage");
        using var failuresDefault = await fixture.Client.GetAsync("/api/operations/messages/failures");
        using var failuresNormal = await fixture.Client.GetAsync("/api/operations/messages/failures?limit=5");
        using var failuresMax = await fixture.Client.GetAsync("/api/operations/messages/failures?limit=1000");
        Assert.All(new[] { failuresDefault, failuresNormal, failuresMax },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        _ = failures.Received(1).ListAsync(100, Arg.Any<CancellationToken>());
        _ = failures.Received(1).ListAsync(5, Arg.Any<CancellationToken>());
        _ = failures.Received(1).ListAsync(1000, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DomainValidationStillMapsToPublicProblemDetails()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authenticate("profiles.manage");
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", new CreateProfile("", "subject", "name", []));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemCodeAsync(response, "validation");
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string code)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ReadinessRejectsAnUnmigratedDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("pending_migrations", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(true, "healthy")]
    [InlineData(false, "degraded")]
    public async Task BrokerOutageDegradesReadinessWithoutRejectingDurableBusinessWrites(bool connected, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(migrated: true, brokerAvailable: connected);
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("status").GetString());
        Assert.Equal("SelfManaged", body.RootElement.GetProperty("messaging").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(false, "pending_migrations")]
    [InlineData(true, "storage_unavailable")]
    public async Task StorageFailureIsUnhealthyAndKeepsMigrationReasonPriority(bool migrated, string reason)
    {
        await using var fixture = await Fixture.CreateAsync(migrated: migrated, storageAvailable: false);
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task BrokerProbeExceptionIsUnhealthyRatherThanDegraded()
    {
        await using var fixture = await Fixture.CreateAsync(migrated: true, brokerAvailable: true);
        await fixture.Broker.DisposeAsync();
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("storage_unavailable", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ApiRateLimitCoversAnonymousRequestsWithoutLimitingHealthOrUnknownPaths()
    {
        await using var fixture = await Fixture.CreateAsync(apiPermitLimit: 2);
        for (var i = 0; i < 3; i++)
        {
            using var health = await fixture.Client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        using var first = await fixture.Client.GetAsync("/api/v1/profiles");
        using var second = await fixture.Client.GetAsync("/api/v1/profiles");
        using var rejected = await fixture.Client.GetAsync("/api/v1/profiles");
        using var unknown = await fixture.Client.GetAsync("/not-found");
        using var healthAfter = await fixture.Client.GetAsync("/health/live");
        using var readiness = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthAfter.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
    }

    [Fact]
    public async Task ApiRateLimitCoversAuthenticatedRequests()
    {
        await using var fixture = await Fixture.CreateAsync(apiPermitLimit: 2);
        fixture.Authenticate("profiles.manage");

        using var first = await fixture.Client.GetAsync("/api/v1/roles");
        using var second = await fixture.Client.GetAsync("/api/v1/roles");
        using var rejected = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Theory]
    [InlineData("invalid", "120", "60")]
    [InlineData("true", "0", "60")]
    [InlineData("true", "120", "0")]
    public void InvalidRateLimitConfigurationFailsDuringComposition(string enabled, string permitLimit, string windowSeconds)
    {
        var builder = Fixture.Builder();
        builder.Configuration["Http:RateLimiting:Enabled"] = enabled;
        builder.Configuration["Http:RateLimiting:PermitLimit"] = permitLimit;
        builder.Configuration["Http:RateLimiting:WindowSeconds"] = windowSeconds;
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    [Fact]
    public async Task RequestTimeoutPolicyAppliesToApiButNotHealth()
    {
        await using var fixture = await Fixture.CreateAsync(apiTimeoutSeconds: 5);
        var api = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/v1/roles");
        var live = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/health/live");
        var ready = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/health/ready");
        Assert.Equal("reference-api-timeout", api.Metadata.GetMetadata<RequestTimeoutAttribute>()?.PolicyName);
        Assert.Null(live.Metadata.GetMetadata<RequestTimeoutAttribute>());
        Assert.Null(ready.Metadata.GetMetadata<RequestTimeoutAttribute>());

        fixture.Authenticate("profiles.manage");
        using var response = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid", "30")]
    [InlineData("true", "0")]
    [InlineData("true", "-1")]
    public void InvalidRequestTimeoutConfigurationFailsDuringComposition(string enabled, string seconds)
    {
        var builder = Fixture.Builder();
        builder.Configuration["Http:RequestTimeouts:Enabled"] = enabled;
        builder.Configuration["Http:RequestTimeouts:Seconds"] = seconds;
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    [Fact]
    public void UnknownProviderFailsDuringComposition()
    {
        var builder = Fixture.Builder("unknown");
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    private sealed class Fixture(WebApplication app, SqliteConnection connection) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Enumerable.Repeat((byte)19, 32).ToArray());
        public HttpClient Client { get; } = app.GetTestClient();
        public RabbitMqTransport Broker => app.Services.GetRequiredService<RabbitMqTransport>();
        public IReadOnlyList<Endpoint> Endpoints => app.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        public static WebApplicationBuilder Builder(string provider = "SelfManaged") => ServiceHost.CreateBuilder([
            "--environment", "Production", "--ConnectionStrings:ReferenceDatabase", "Host=unused;Database=reference;Username=test",
            "--Messaging:Provider", provider, "--Messaging:RabbitMQ:ConnectionString", "amqp://localhost",
            "--Authentication:Authority", "https://issuer.example", "--Authentication:Audience", "ms-reference",
            "--OpenTelemetry:Enabled", "false"
        ]);

        public static async Task<Fixture> CreateAsync(bool migrated = false, bool brokerAvailable = false,
            int? apiPermitLimit = null, int? apiTimeoutSeconds = null, bool storageAvailable = true,
            Action<IServiceCollection>? configureServices = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = Builder();
            if (apiPermitLimit is { } limit)
            {
                builder.Configuration["Http:RateLimiting:Enabled"] = "true";
                builder.Configuration["Http:RateLimiting:PermitLimit"] = limit.ToString();
                builder.Configuration["Http:RateLimiting:WindowSeconds"] = "60";
            }
            if (apiTimeoutSeconds is { } seconds)
            {
                builder.Configuration["Http:RequestTimeouts:Enabled"] = "true";
                builder.Configuration["Http:RequestTimeouts:Seconds"] = seconds.ToString();
            }
            builder.WebHost.UseTestServer();
            ReferenceHost.AddServices(builder);
            foreach (var descriptor in builder.Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace?.StartsWith("MS.Microservice.Messaging", StringComparison.Ordinal) == true
                || descriptor.ServiceType.IsGenericType && descriptor.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && descriptor.ServiceType.GenericTypeArguments.Contains(typeof(SelfManagedReferenceDbContext))).ToArray())
                builder.Services.Remove(descriptor);
            builder.Services.RemoveAll<DbContextOptions<SelfManagedReferenceDbContext>>();
            builder.Services.RemoveAll<SelfManagedReferenceDbContext>();
            builder.Services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseSqlite(connection));
            if (!storageAvailable)
            {
                builder.Services.RemoveAll<IMessageStorageProbe>();
                var storage = Substitute.For<IMessageStorageProbe>();
                storage.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("storage offline")));
                builder.Services.AddSingleton(storage);
            }
            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
                configuration.SigningKeys.Add(Key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            var brokerFactory = Substitute.For<IConnectionFactory>();
            if (brokerAvailable)
            {
                var brokerConnection = Substitute.For<IConnection>();
                var channel = Substitute.For<IChannel>();
                channel.IsOpen.Returns(true);
                brokerConnection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
                brokerFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(brokerConnection);
            }
            else brokerFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IConnection>(new IOException("offline")));
            builder.Services.AddSingleton(brokerFactory);
            configureServices?.Invoke(builder.Services);
            var app = builder.Build();
            ReferenceHost.MapApplication(app);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
                await context.Database.EnsureCreatedAsync();
                if (migrated)
                {
                    var history = context.GetService<IHistoryRepository>();
                    await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
                    foreach (var migration in context.Database.GetMigrations())
                        await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migration, "10.0.6")));
                }
            }
            await app.StartAsync();
            return new(app, connection);
        }

        public void Authenticate(string scope)
        {
            var token = new JwtSecurityToken("https://issuer.example", "ms-reference",
                [new("sub", "test-admin"), new("scope", scope)], expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new(Key, SecurityAlgorithms.HmacSha256));
            Client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
