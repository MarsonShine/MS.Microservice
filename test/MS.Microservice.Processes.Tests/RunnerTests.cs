using MS.Microservice.Processes;
using Xunit;
namespace MS.Microservice.Processes.Tests;
public class RunnerTests
{
    public static TheoryData<TimeSpan, TimeSpan> InvalidDeadlines => new()
    {
        { TimeSpan.Zero, TimeSpan.FromSeconds(10) },
        { TimeSpan.FromMilliseconds(-2), TimeSpan.FromSeconds(10) },
        { TimeSpan.FromMilliseconds(uint.MaxValue), TimeSpan.FromSeconds(10) },
        { TimeSpan.MaxValue, TimeSpan.FromSeconds(10) },
        { TimeSpan.FromSeconds(1), TimeSpan.Zero },
        { TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan },
        { TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(uint.MaxValue) },
        { TimeSpan.FromSeconds(1), TimeSpan.MaxValue }
    };

    [Theory]
    [MemberData(nameof(InvalidDeadlines))]
    public async Task InvalidDeadlinesFailBeforePreparingOrStartingAProcess(TimeSpan timeout, TimeSpan cleanupTimeout)
    {
        var enumerated = false;
        IEnumerable<string> Arguments()
        {
            enumerated = true;
            yield return "--version";
        }

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ProcessRunner.RunAsync("dotnet", Arguments(), new() { Timeout = timeout, CleanupTimeout = cleanupTimeout }));
        Assert.Equal("options", error.ParamName);
        Assert.False(enumerated);
    }

    [Fact]
    public async Task InfiniteExecutionTimeoutReturnsTheProcessResult()
    {
        var result = await Run("echo", new() { Timeout = Timeout.InfiniteTimeSpan });
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("中文 with spaces", result.StandardOutput);
        Assert.Equal("diagnostic", result.StandardError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public async Task MaximumFiniteDeadlinesAreAccepted(double fractionalMilliseconds)
    {
        var maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1L) + TimeSpan.FromMilliseconds(fractionalMilliseconds);
        var result = await Run("echo", new() { Timeout = maximum, CleanupTimeout = maximum });
        Assert.Equal(7, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public async Task MaximumCleanupDeadlinePreservesTheExecutionTimeout(double fractionalMilliseconds)
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Run("sleep", new()
        {
            Timeout = TimeSpan.FromSeconds(1),
            CleanupTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1L) + TimeSpan.FromMilliseconds(fractionalMilliseconds)
        }));
    }

    private static Task<ProcessResult> Run(string mode, ProcessRunOptions? options = null, CancellationToken token = default) =>
        ProcessRunner.RunAsync("dotnet", ["exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "MS.Microservice.Processes.Tests.runtimeconfig.json"), typeof(ProcessFixture.Program).Assembly.Location, mode, "中文 with spaces"], options, token);
    [Fact] public async Task CapturesBothStreamsAndNonzeroExit()
    {
        var result = await Run("echo");
        Assert.Equal(7, result.ExitCode); Assert.Equal("中文 with spaces", result.StandardOutput); Assert.Equal("diagnostic", result.StandardError);
    }
    [Fact] public async Task DrainsBothPipesWithoutDeadlock()
    {
        var result = await Run("flood"); Assert.Equal(100000, result.StandardOutput.Length); Assert.Equal(100000, result.StandardError.Length);
    }
    [Fact] public async Task OutputLimitStopsProcess() => await Assert.ThrowsAsync<InvalidDataException>(() => Run("flood", new() { MaximumOutputCharacters = 100 }));
    [Fact] public async Task TimeoutIsDistinctFromCancellation() => await Assert.ThrowsAsync<TimeoutException>(() => Run("sleep", new() { Timeout = TimeSpan.FromSeconds(1) }));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationStopsProcess(bool infiniteTimeout)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var options = infiniteTimeout ? new ProcessRunOptions { Timeout = Timeout.InfiniteTimeSpan } : null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("sleep", options, stop.Token));
    }
    [Fact] public async Task MissingExecutableFails() => await Assert.ThrowsAnyAsync<Exception>(() => ProcessRunner.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe"), []));
    [Fact] public async Task EnvironmentOverridesArePassed() => Assert.Equal("configured", (await Run("environment", new() { Environment = new Dictionary<string,string?> { ["RUNNER_TEST"] = "configured" } })).StandardOutput);
    [Fact] public async Task PreCanceledDoesNotStart()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync("missing-executable", [], cancellationToken: new(true)));
    }
}
