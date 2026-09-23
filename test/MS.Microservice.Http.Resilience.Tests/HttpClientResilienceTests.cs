using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MS.Microservice.Http.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace MS.Microservice.Http.Resilience.Tests;

public sealed class HttpClientResilienceTests
{
    [Fact]
    public async Task Get_RetriesOneTransientFailure()
    {
        var calls = 0;
        using var provider = CreateProvider(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(++calls == 1 ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK))));
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        using var response = await client.GetAsync("https://example.test/item");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task UnsafeMethod_DoesNotRetry(string method)
    {
        var calls = 0;
        using var provider = CreateProvider(new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }));
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://example.test/item");

        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AttemptTimeout_CancelsHungTransport()
    {
        var calls = 0;
        using var provider = CreateProvider(new StubHandler(async (_, token) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }), options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync("https://example.test/item"));
        Assert.InRange(calls, 1, 2);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotSendOrRetry()
    {
        var calls = 0;
        using var provider = CreateProvider(new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }));
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/item", cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CircuitBreaker_StopsSendingAfterRepeatedFailures()
    {
        var calls = 0;
        using var provider = CreateProvider(new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }), options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.CircuitBreaker.MinimumThroughput = 2;
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        var opened = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("https://example.test/item");
                Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
            }
            catch (BrokenCircuitException)
            {
                opened = true;
                break;
            }
        }
        Assert.True(opened);
        var callsAfterOpen = calls;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("https://example.test/item"));
        Assert.Equal(callsAfterOpen, calls);
    }

    private static ServiceProvider CreateProvider(HttpMessageHandler handler,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("probe")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddMsHttpResilience(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                configure?.Invoke(options);
            });
        return services.BuildServiceProvider();
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
