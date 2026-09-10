using System.Collections.Concurrent;
using System.Diagnostics;
using MS.Microservice.Messaging.FaultWorker;

namespace MS.Microservice.Messaging.IntegrationTests;

internal sealed class WorkerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> signals = new();
    private bool killed;

    public WorkerProcess(FaultEnvironment? environment = null, string? phase = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MS.Microservice.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var binary = new DirectoryInfo(AppContext.BaseDirectory);
        var worker = Path.Combine(root.FullName, "MS.Microservice.Messaging/test/MS.Microservice.Messaging.FaultWorker/bin",
            binary.Parent!.Name, binary.Name, "MS.Microservice.Messaging.FaultWorker.dll");
        if (!File.Exists(worker)) throw new FileNotFoundException("Build the fault worker first.", worker);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(worker)!
        };
        start.ArgumentList.Add(worker);
        if (environment is null) start.ArgumentList.Add("--barrier-only");
        else
        {
            start.Environment["MS_FAULT_PROVIDER"] = environment.Provider;
            start.Environment["MS_FAULT_DATABASE"] = environment.ConnectionString;
            start.Environment["MS_FAULT_BROKER"] = environment.BrokerConnectionString;
            start.Environment["MS_FAULT_PREFIX"] = environment.Prefix;
            start.Environment["MS_FAULT_PHASE"] = phase;
        }
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Receive(args.Data);
        process.ErrorDataReceived += (_, args) => Receive(args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void Receive(string? line)
    {
        if (line is null) return;
        output.Enqueue(line);
        while (output.Count > 100) output.TryDequeue(out _);
        var key = line.Split('|')[0];
        if (key is "READY" or "BARRIER" or "COMMITTED")
            signals.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(line);
    }

    public async Task<string> SignalAsync(string key)
    {
        var signal = signals.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        var first = await Task.WhenAny(signal, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(90));
        if (first == signal) return await signal;
        throw new InvalidOperationException($"Worker exited {process.ExitCode}: {string.Join(Environment.NewLine, output)}");
    }

    public async Task CommandAsync(string command)
    {
        await process.StandardInput.WriteLineAsync(command);
        await process.StandardInput.FlushAsync();
    }

    public async Task KillAsync()
    {
        killed = true;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, process.ExitCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            try
            {
                await CommandAsync("stop");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (Exception) { await KillAsync(); }
        }
        if (!killed) Assert.Equal(0, process.ExitCode);
        process.Dispose();
    }
}
