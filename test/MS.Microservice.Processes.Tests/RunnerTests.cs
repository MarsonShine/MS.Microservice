using MS.Microservice.Processes;
using Xunit;
namespace MS.Microservice.Processes.Tests;
public class RunnerTests
{
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
    [Fact] public async Task CancellationStopsProcess()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("sleep", token: stop.Token));
    }
    [Fact] public async Task MissingExecutableFails() => await Assert.ThrowsAnyAsync<Exception>(() => ProcessRunner.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe"), []));
    [Fact] public async Task EnvironmentOverridesArePassed() => Assert.Equal("configured", (await Run("environment", new() { Environment = new Dictionary<string,string?> { ["RUNNER_TEST"] = "configured" } })).StandardOutput);
    [Fact] public async Task PreCanceledDoesNotStart()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync("missing-executable", [], cancellationToken: new(true)));
    }
}
