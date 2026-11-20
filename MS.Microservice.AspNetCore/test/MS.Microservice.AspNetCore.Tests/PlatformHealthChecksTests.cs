using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class PlatformHealthChecksTests
{
    [Fact]
    public async Task LivenessSkipsDependenciesAndReadinessRunsOnlyTaggedChecks()
    {
        var readyCalls = 0;
        var unrelatedCalls = 0;
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformHealthChecks()
            .Add(Registration("ready-check", [PlatformHealthChecks.ReadyTag], _ =>
            {
                Interlocked.Increment(ref readyCalls);
                return Task.FromResult(HealthCheckResult.Healthy());
            }))
            .Add(Registration("unrelated", ["other"], _ =>
            {
                Interlocked.Increment(ref unrelatedCalls);
                return Task.FromResult(HealthCheckResult.Unhealthy());
            }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapPlatformHealthChecks();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await live.Content.ReadAsStringAsync());
        Assert.Equal(0, Volatile.Read(ref readyCalls));
        Assert.Equal(0, Volatile.Read(ref unrelatedCalls));

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await ready.Content.ReadAsStringAsync());
        Assert.Equal(1, Volatile.Read(ref readyCalls));
        Assert.Equal(0, Volatile.Read(ref unrelatedCalls));
    }

    [Theory]
    [InlineData(HealthStatus.Degraded, HttpStatusCode.OK, "degraded")]
    [InlineData(HealthStatus.Unhealthy, HttpStatusCode.ServiceUnavailable, "unhealthy")]
    public async Task ReadinessMapsDependencyStatus(HealthStatus healthStatus, HttpStatusCode expectedCode, string expectedStatus)
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformHealthChecks().Add(Registration("dependency", [PlatformHealthChecks.ReadyTag],
            _ => Task.FromResult(new HealthCheckResult(healthStatus))));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapPlatformHealthChecks();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(expectedCode, response.StatusCode);
        Assert.Equal($"{{\"status\":\"{expectedStatus}\"}}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StoppingShortCircuitsReadinessWithoutRunningDependencies()
    {
        var calls = 0;
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformHealthChecks().Add(Registration("dependency", [PlatformHealthChecks.ReadyTag], _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(HealthCheckResult.Healthy());
        }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapPlatformHealthChecks();
        await app.StartAsync();
        using var client = app.GetTestClient();
        app.Lifetime.StopApplication();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("{\"status\":\"unhealthy\",\"reason\":\"stopping\"}", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task CallerCancellationReachesDependencyCheck()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformHealthChecks().Add(Registration("slow", [PlatformHealthChecks.ReadyTag], async token =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.TrySetResult(token.IsCancellationRequested); }
            return HealthCheckResult.Healthy();
        }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapPlatformHealthChecks();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource();

        var pending = client.GetAsync("/health/ready", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task GlobalTrafficPoliciesDoNotLimitOrTimeoutHealthEndpoints()
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddPlatformRateLimiting(options => options.GlobalLimiter =
            PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("all", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                })));
        builder.Services.AddPlatformRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromMilliseconds(250),
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
        });
        builder.Services.AddPlatformHealthChecks().Add(Registration("slow-ready", [PlatformHealthChecks.ReadyTag],
            async token =>
            {
                await Task.Delay(400, token);
                return HealthCheckResult.Healthy();
            }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRequestTimeouts();
        app.UsePlatformRateLimiting();
        app.MapPlatformHealthChecks();
        app.MapGet("/fast", () => Results.Ok());
        app.MapGet("/slow", async (HttpContext context) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            return Results.Ok();
        });
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        using var first = await client.GetAsync("/fast");
        using var timeout = await client.GetAsync("/slow");
        using var limited = await client.GetAsync("/fast");
        using var liveAfterLimit = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.GatewayTimeout, timeout.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveAfterLimit.StatusCode);
    }

    private static HealthCheckRegistration Registration(string name, string[] tags,
        Func<CancellationToken, Task<HealthCheckResult>> check)
        => new(name, _ => new DelegateHealthCheck(check), HealthStatus.Unhealthy, tags, TimeSpan.FromSeconds(5));

    private sealed class DelegateHealthCheck(Func<CancellationToken, Task<HealthCheckResult>> check) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => check(cancellationToken);
    }

}
