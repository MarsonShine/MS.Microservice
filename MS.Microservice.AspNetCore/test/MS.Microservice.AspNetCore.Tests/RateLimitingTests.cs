using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task NamedPolicyLimitsOnlySelectedEndpointAndIgnoresClientSuppliedIpHeader()
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddPlatformRateLimiting(options =>
            options.AddFixedWindowLimiter("selected", limiter =>
            {
                limiter.PermitLimit = 1;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRateLimiting();
        app.MapGet("/selected", () => Results.Ok()).RequireRateLimiting("selected");
        app.MapGet("/open", () => Results.Ok());
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var first = new HttpRequestMessage(HttpMethod.Get, "/selected");
        first.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.10");
        using var firstResponse = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var second = new HttpRequestMessage(HttpMethod.Get, "/selected");
        second.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.11");
        using var rejected = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.TryParse(Assert.Single(retryAfter), out var seconds) && seconds > 0);

        using var open = await client.GetAsync("/open");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    [Fact]
    public async Task GlobalLimiterAppliesToAllEndpointsExceptExplicitExemption()
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddPlatformRateLimiting(options =>
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                })));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRateLimiting();
        app.MapGet("/first", () => Results.Ok());
        app.MapGet("/second", () => Results.Ok());
        app.MapGet("/health", () => Results.Ok()).DisableRateLimiting();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/first");
        using var rejected = await client.GetAsync("/second");
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
