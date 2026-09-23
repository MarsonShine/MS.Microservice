using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class RequestTimeoutTests
{
    [Fact]
    public async Task EndpointTimeoutCancelsRequestAbortedAndReturns504()
    {
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddPlatformRequestTimeouts(options =>
            options.AddPolicy("short", new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromMilliseconds(50),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
            }));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRequestTimeouts();
        app.MapGet("/slow", async (HttpContext context) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            finally { canceled.TrySetResult(context.RequestAborted.IsCancellationRequested); }
            return Results.Ok();
        }).WithRequestTimeout("short");
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/slow");
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task GlobalTimeoutAllowsNormalEndpointAndExplicitHealthExemption()
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddPlatformRequestTimeouts(options =>
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromMilliseconds(250),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
            });
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRequestTimeouts();
        app.MapGet("/fast", () => Results.Ok());
        app.MapGet("/health", async () =>
        {
            await Task.Delay(400);
            return Results.Ok();
        }).DisableRequestTimeout();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var fast = await client.GetAsync("/fast");
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, fast.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutReturningTimeoutResponse()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddPlatformRequestTimeouts(options =>
            options.AddPolicy("long", TimeSpan.FromSeconds(30)));
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UsePlatformRequestTimeouts();
        app.MapGet("/wait", async (HttpContext context) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            finally { canceled.TrySetResult(context.RequestAborted.IsCancellationRequested); }
            return Results.Ok();
        }).WithRequestTimeout("long");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource();

        var pending = client.GetAsync("/wait", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
