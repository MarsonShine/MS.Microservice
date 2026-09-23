using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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
using MS.Microservice.Reference.Application;
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
        using var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var denied = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        fixture.Authenticate("messaging.manage");
        using var allowed = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task ReadinessRejectsAnUnmigratedDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("pending_migrations", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true, "healthy")]
    [InlineData(false, "degraded")]
    public async Task BrokerOutageDegradesReadinessWithoutRejectingDurableBusinessWrites(bool connected, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(migrated: true, brokerAvailable: connected);
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
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
    public void UnknownProviderFailsDuringComposition()
    {
        var builder = Fixture.Builder("unknown");
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    private sealed class Fixture(WebApplication app, SqliteConnection connection) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Enumerable.Repeat((byte)19, 32).ToArray());
        public HttpClient Client { get; } = app.GetTestClient();

        public static WebApplicationBuilder Builder(string provider = "SelfManaged") => ServiceHost.CreateBuilder([
            "--environment", "Production", "--ConnectionStrings:ReferenceDatabase", "Host=unused;Database=reference;Username=test",
            "--Messaging:Provider", provider, "--Messaging:RabbitMQ:ConnectionString", "amqp://localhost",
            "--Authentication:Authority", "https://issuer.example", "--Authentication:Audience", "ms-reference",
            "--OpenTelemetry:Enabled", "false"
        ]);

        public static async Task<Fixture> CreateAsync(bool migrated = false, bool brokerAvailable = false, int? apiPermitLimit = null)
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
