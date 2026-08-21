using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MS.Microservice.Infrastructure.HealthChecks;
using MS.Microservice.Web.Infrastructure.Extensions;
using MS.Microservice.Web.Infrastructure.HealthChecks;
using System.Text.Json;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class PlatformHealthCheckTests
{
    [Fact]
    public async Task SqlHealthCheck_WhenConnectionIsMissing_ReturnsGenericUnhealthyResult()
    {
        var healthCheck = new SqlHealthCheck(new ConfigurationBuilder().Build());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("PostgreSQL database configuration is missing.", result.Description);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void AddHealthChecks_AssignsLiveAndReadyTags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks(CreateConfiguration());
        using var serviceProvider = services.BuildServiceProvider();

        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var self = Assert.Single(registrations, registration => registration.Name == "self");
        var postgresql = Assert.Single(
            registrations,
            registration => registration.Name == SqlHealthCheck.Name);

        Assert.Contains("live", self.Tags);
        Assert.DoesNotContain(SqlHealthCheck.ReadinessTag, self.Tags);
        Assert.Contains(SqlHealthCheck.ReadinessTag, postgresql.Tags);
        Assert.DoesNotContain("live", postgresql.Tags);
    }

    [Fact]
    public void CreateOptions_FiltersChecksByRequiredTag()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks(CreateConfiguration());
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var self = registrations.Single(registration => registration.Name == "self");
        var postgresql = registrations.Single(registration => registration.Name == SqlHealthCheck.Name);

        var live = PlatformHealthCheckEndpoints.CreateOptions("live");
        var ready = PlatformHealthCheckEndpoints.CreateOptions(SqlHealthCheck.ReadinessTag);
        var livePredicate = Assert.IsType<Func<HealthCheckRegistration, bool>>(live.Predicate);
        var readyPredicate = Assert.IsType<Func<HealthCheckRegistration, bool>>(ready.Predicate);

        Assert.True(livePredicate(self));
        Assert.False(livePredicate(postgresql));
        Assert.False(readyPredicate(self));
        Assert.True(readyPredicate(postgresql));
    }

    [Fact]
    public async Task WriteResponseAsync_DoesNotExposeDescriptionExceptionOrData()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy,
            "Host=secret-db;Password=secret-password",
            TimeSpan.FromMilliseconds(12),
            new InvalidOperationException("connection secret details"),
            new Dictionary<string, object> { ["connectionString"] = "secret-value" });
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry> { [SqlHealthCheck.Name] = entry },
            TimeSpan.FromMilliseconds(13));

        await PlatformHealthCheckEndpoints.WriteResponseAsync(context, report);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var json = document.RootElement.GetRawText();
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(SqlHealthCheck.Name, document.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointPaths_AreStableAndCompatibilityAliasIsRetained()
    {
        Assert.Equal("/health/live", PlatformHealthCheckEndpoints.LivenessPath);
        Assert.Equal("/health/ready", PlatformHealthCheckEndpoints.ReadinessPath);
        Assert.Equal("/hc", PlatformHealthCheckEndpoints.CompatibilityPath);
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivationConnection"] = "Host=localhost;Database=test;Username=test"
            })
            .Build();
}
