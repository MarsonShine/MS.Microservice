#if VERIFY_CACHING || VERIFY_HTTP
using Microsoft.Extensions.DependencyInjection;
#endif
#if VERIFY_THREADING
using MS.Microservice.Threading;
#endif
#if VERIFY_CACHING
using MS.Microservice.Caching;
#endif
#if VERIFY_HTTP
using System.Net;
using MS.Microservice.Http.Resilience;
#endif

public static class PlatformConsumer
{
    public static async Task<IReadOnlyList<string>> VerifyAsync()
    {
        var verified = new List<string>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
#if VERIFY_THREADING
        await VerifyThreadingAsync(deadline.Token);
        verified.AddRange(["threading.singleflight", "threading.lease-release"]);
#endif
#if VERIFY_CACHING
        await VerifyCachingAsync(deadline.Token);
        verified.AddRange(["caching.di-source-coalescing", "caching.scope-isolation"]);
#endif
#if VERIFY_HTTP
        await VerifyHttpAsync(deadline.Token);
        verified.AddRange(["http.get-retry", "http.post-no-retry"]);
#endif
        foreach (var check in verified) Console.WriteLine("Passed: " + check);
        return verified;
    }

#if VERIFY_THREADING
    private static async Task VerifyThreadingAsync(CancellationToken token)
    {
        var manager = new SingleflightManager();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<int> Load()
        {
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(token);
            return 42;
        }
        var requests = Enumerable.Range(0, 8).Select(_ => manager.ExecuteOnceAsync("same-key", Load, token)).ToArray();
        release.SetResult();
        var values = await Task.WhenAll(requests);
        if (calls != 1 || values.Any(value => value != 42)) throw new Exception("Singleflight did not share one source operation.");

        using var semaphore = new SemaphoreSlim(1, 1);
        using var first = await semaphore.AcquireAsync(token);
        var waiting = semaphore.AcquireAsync(token).AsTask();
        if (waiting.IsCompleted) throw new Exception("The occupied lease did not retain its permit.");
        first.Dispose();
        using var second = await waiting;
        second.Dispose();
        if (semaphore.CurrentCount != 1) throw new Exception("Lease disposal did not return exactly one permit.");
    }
#endif

#if VERIFY_CACHING
    private static async Task VerifyCachingAsync(CancellationToken token)
    {
        var services = new ServiceCollection();
        services.AddGovernedCaching(options => options.ApplicationName = "package-consumer");
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IGovernedCache>();
        var key = cache.CreateKey<string>("consumer.v1", "scope-a", "same-resource");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async ValueTask<string> Load(CancellationToken cancellation)
        {
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(cancellation);
            return "scope-a-value";
        }
        var requests = Enumerable.Range(0, 8).Select(_ => cache.GetOrCreateAsync(key, Load, token).AsTask()).ToArray();
        release.SetResult();
        var values = await Task.WhenAll(requests).WaitAsync(token);
        if (calls != 1 || values.Any(value => value != "scope-a-value")) throw new Exception("Caching did not coalesce source loads through DI.");
        var otherKey = cache.CreateKey<string>("consumer.v1", "scope-b", "same-resource");
        var other = await cache.GetOrCreateAsync(otherKey, _ => ValueTask.FromResult("scope-b-value"), token);
        var cached = await cache.GetOrCreateAsync(key, _ => ValueTask.FromException<string>(new Exception("The cached key loaded again.")), token);
        if (other != "scope-b-value" || cached != "scope-a-value") throw new Exception("Caching did not isolate the caller's scopes.");
    }
#endif

#if VERIFY_HTTP
    private static async Task VerifyHttpAsync(CancellationToken token)
    {
        var handler = new RetryProbeHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("consumer")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddMsHttpResilience(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("consumer");
        using var get = await client.GetAsync("https://consumer.invalid/probe", token);
        if (get.StatusCode != HttpStatusCode.OK || handler.GetCalls != 2) throw new Exception("GET did not retry its transient failure.");
        using var post = await client.PostAsync("https://consumer.invalid/probe", new StringContent("payload"), token);
        if (post.StatusCode != HttpStatusCode.ServiceUnavailable || handler.PostCalls != 1) throw new Exception("POST was automatically retried.");
    }

    private sealed class RetryProbeHandler : HttpMessageHandler
    {
        public int GetCalls { get; private set; }
        public int PostCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(++GetCalls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            if (request.Method != HttpMethod.Post) throw new Exception("Unexpected HTTP method.");
            PostCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
#endif
}
