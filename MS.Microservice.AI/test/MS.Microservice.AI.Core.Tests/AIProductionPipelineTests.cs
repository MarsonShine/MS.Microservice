using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class AIProductionPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReporterFailureCannotChangeProviderOutcome(bool providerFails)
    {
        var state = new State { ReportFailure = new InvalidOperationException("report unavailable") };
        var pipeline = Create(state);
        var original = new AIProviderException("provider failed", AIErrorCodes.ProviderUnavailable);
        var call = pipeline.ExecuteAsync<int>(Context, _ => providerFails
            ? ValueTask.FromException<int>(original) : ValueTask.FromResult(42), _ => AIUsage.Zero, default).AsTask();
        if (providerFails) Assert.Same(original, await Assert.ThrowsAsync<AIProviderException>(() => call));
        else Assert.Equal(42, await call);
        Assert.Equal(providerFails ? 0 : 1, state.Successes);
        Assert.Equal(providerFails ? 1 : 0, state.Failures);
        Assert.Equal(1, state.Releases);
        Assert.Equal(1, state.Reports);
    }

    [Fact]
    public async Task CallerCancellationIsNotProviderFailure()
    {
        var state = new State();
        var pipeline = Create(state);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteAsync<int>(Context, token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(1);
        }, _ => AIUsage.Zero, cancellation.Token).AsTask());
        Assert.Equal(0, state.Failures);
        Assert.Equal(0, state.Successes);
        Assert.Equal(1, state.Releases);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("break")]
    [InlineData("provider")]
    [InlineData("factory")]
    [InlineData("usage")]
    public async Task StreamOwnsLeaseAndClassifiesOnlyProviderFailures(string mode)
    {
        var state = new State { ReportFailure = new InvalidOperationException("report unavailable") };
        var pipeline = Create(state);
        async Task Consume()
        {
            await foreach (var item in pipeline.ExecuteStreamAsync(Context,
                _ => mode == "factory" ? throw new FormatException("factory") : Chunks(mode),
                _ => mode == "usage" ? throw new FormatException("usage") : AIUsage.Zero, default))
            {
                if (mode == "break") break;
            }
        }
        if (mode is "provider" or "factory" or "usage") await Assert.ThrowsAsync<FormatException>(Consume);
        else await Consume();
        Assert.Equal(mode == "complete" ? 1 : 0, state.Successes);
        Assert.Equal(mode is "provider" or "factory" or "usage" ? 1 : 0, state.Failures);
        Assert.Equal(1, state.Releases);
        Assert.Equal(1, state.Reports);
    }

    private static async IAsyncEnumerable<int> Chunks(string mode)
    {
        yield return 1;
        await Task.Yield();
        if (mode == "provider") throw new FormatException("provider");
        yield return 2;
    }

    private static readonly AIRequestContext Context = new() { Provider = "test", Model = "test", Capability = AICapability.Chat };

    private static AIProductionPipeline Create(State state)
    {
        var services = new ServiceCollection();
        services.AddOptions<AICostAccountingOptions>().Configure(options => options.Enabled = true);
        using var provider = services.BuildServiceProvider();
        return new(state, state, state, provider.GetRequiredService<IOptionsMonitor<AICostAccountingOptions>>(), TimeProvider.System);
    }

    private sealed class State : IAIRateLimiter, IAICircuitBreaker, IAICostReporter
    {
        public int Successes, Failures, Releases, Reports;
        public Exception? ReportFailure;
        public ValueTask<AIRateLimitLease> AcquireAsync(AIRequestContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AIRateLimitLease(() => { Releases++; return ValueTask.CompletedTask; }));
        public ValueTask EnsureAllowedAsync(AIRequestContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RecordSuccessAsync(AIRequestContext context, CancellationToken cancellationToken = default)
        { Successes++; return ValueTask.CompletedTask; }
        public ValueTask RecordFailureAsync(AIRequestContext context, Exception exception, CancellationToken cancellationToken = default)
        { Failures++; return ValueTask.CompletedTask; }
        public ValueTask ReportAsync(AICostRecord record, CancellationToken cancellationToken = default)
        { Reports++; return ReportFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(ReportFailure); }
    }
}
