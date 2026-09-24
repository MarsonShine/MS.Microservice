using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MS.Microservice.HealthChecks.Redis;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace MS.Microservice.HealthChecks.Redis.Tests;

public sealed class RedisHealthCheckTests
{
    [Fact]
    public async Task RegisteredProbeUsesExistingConnectionToPingRedis()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        database.PingAsync().Returns(Task.FromResult(TimeSpan.FromMilliseconds(1)));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(connection);
        services.AddHealthChecks().AddRedisHealthCheck();
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries[RedisHealthChecks.DefaultName].Status);
        await database.Received(1).PingAsync();
    }

    [Fact]
    public async Task PingFailureIsUnhealthyWithoutExposingConnectionDetails()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        database.PingAsync().Returns(Task.FromException<TimeSpan>(
            new InvalidOperationException("redis://user:secret@cache.local")));

        var result = await new RedisHealthCheck(connection).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Redis PING failed.", result.Description);
        Assert.Null(result.Exception);
        Assert.DoesNotContain("secret", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationStopsWaitingForPing()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        var ping = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        database.PingAsync().Returns(ping.Task);
        using var cancellation = new CancellationTokenSource();

        var check = new RedisHealthCheck(connection).CheckHealthAsync(new HealthCheckContext(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.False(ping.Task.IsCompleted);
    }

    [Fact]
    public async Task CancellationBeforeProbeDoesNotSendPing()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RedisHealthCheck(connection).CheckHealthAsync(new HealthCheckContext(), cancellation.Token));

        connection.DidNotReceive().GetDatabase();
        await database.DidNotReceive().PingAsync();
    }

    [Fact]
    public async Task RedisProbeIsAbsentUntilExplicitlyRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            throw new InvalidOperationException("Redis client should not be resolved."));
        services.AddHealthChecks().AddCheck("other", () => HealthCheckResult.Healthy());
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Single(report.Entries);
        Assert.False(report.Entries.ContainsKey(RedisHealthChecks.DefaultName));
    }

    [Fact]
    public async Task RegisteredProbeWithoutRedisClientIsUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddRedisHealthCheck();
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries[RedisHealthChecks.DefaultName].Status);
    }

    [Fact]
    public async Task RedisClientConstructionFailureIsUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            throw new InvalidOperationException("Password=secret"));
        services.AddHealthChecks().AddRedisHealthCheck();
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var entry = report.Entries[RedisHealthChecks.DefaultName];

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal("Redis PING failed.", entry.Description);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public void ExplicitRegistrationSetsReadinessFailureAndTimeoutPolicy()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddRedisHealthCheck();
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations);

        Assert.Equal(RedisHealthChecks.DefaultName, registration.Name);
        Assert.Contains(RedisHealthChecks.ReadinessTag, registration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal(TimeSpan.FromSeconds(5), registration.Timeout);
    }
}
