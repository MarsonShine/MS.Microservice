using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class DbConnectionHealthCheckTests
{
    [Fact]
    public async Task DatabaseCheckRunsOnReadinessButNotLiveness()
    {
        var connections = 0;
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformHealthChecks().AddDbConnectionCheck("sqlite", _ =>
        {
            Interlocked.Increment(ref connections);
            return new SqliteConnection("Data Source=:memory:");
        });
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapPlatformHealthChecks();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(0, Volatile.Read(ref connections));

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(1, Volatile.Read(ref connections));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionOrCommandFailureIsUnhealthyWithoutLeakingDetails(bool factoryFails)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddDbConnectionCheck("database", _ =>
            factoryFails ? throw new InvalidOperationException("Password=secret")
                : new SqliteConnection("Data Source=:memory:"), "SELECT missing_secret_table");
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var entry = report.Entries["database"];

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal("Database is unavailable.", entry.Description);
        Assert.DoesNotContain("secret", entry.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationIsOptionalAndUsesReadinessDefaults()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks();
        using (var provider = services.BuildServiceProvider())
            Assert.Null(provider.GetService<IOptions<HealthCheckServiceOptions>>());

        services.AddHealthChecks().AddDbConnectionCheck("db", _ => new SqliteConnection("Data Source=:memory:"));
        using var registered = services.BuildServiceProvider();
        var check = Assert.Single(registered.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations);
        Assert.Equal("db", check.Name);
        Assert.Contains(PlatformHealthChecks.ReadyTag, check.Tags);
        Assert.Equal(HealthStatus.Unhealthy, check.FailureStatus);
        Assert.Equal(TimeSpan.FromSeconds(5), check.Timeout);
    }

    [Fact]
    public void InvalidRegistrationIsRejectedAtComposition()
    {
        var services = new ServiceCollection();
        var checks = services.AddHealthChecks();
        Assert.Throws<ArgumentException>(() => checks.AddDbConnectionCheck(" ", _ => new SqliteConnection()));
        Assert.Throws<ArgumentNullException>(() => checks.AddDbConnectionCheck("db", null!));
        Assert.Throws<ArgumentException>(() => checks.AddDbConnectionCheck("db", _ => new SqliteConnection(), " "));
    }
}
