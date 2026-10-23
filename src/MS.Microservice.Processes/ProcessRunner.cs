using System.Diagnostics;
using System.Text;

namespace MS.Microservice.Processes;

public sealed record ProcessRunOptions
{
    /// <summary>Positive execution deadline, or Timeout.InfiniteTimeSpan to rely only on caller cancellation.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Positive finite cleanup deadline within the CancellationTokenSource timer range.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaximumOutputCharacters { get; init; } = 262144;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a command without a shell, capturing bounded UTF-8 text from both output streams.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        options ??= new();
        if ((options.Timeout != Timeout.InfiniteTimeSpan && !IsValidFiniteTimeout(options.Timeout))
            || !IsValidFiniteTimeout(options.CleanupTimeout) || options.MaximumOutputCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (options.WorkingDirectory is not null) start.WorkingDirectory = options.WorkingDirectory;
        if (options.Environment is not null)
            foreach (var pair in options.Environment)
                if (pair.Value is null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        if (!process.Start()) throw new IOException("The process could not be started.");
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = CaptureAsync(process.StandardOutput);
        var errors = CaptureAsync(process.StandardError);
        var all = Task.WhenAll(output, errors, process.WaitForExitAsync(operation.Token));
        try
        {
            var completed = await Task.WhenAny(all, fault.Task).WaitAsync(operation.Token).ConfigureAwait(false);
            if (completed == fault.Task) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(await fault.Task).Throw();
            await all.ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            return new(process.ExitCode, await output, await errors);
        }
        catch (Exception error)
        {
            Exception primary = cancellationToken.IsCancellationRequested
                ? new OperationCanceledException("Process execution canceled.", error, cancellationToken)
                : deadline.IsCancellationRequested ? new TimeoutException("Process execution timed out.", error) : error;
            var cleanupErrors = new List<Exception>();
            operation.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The process may have exited between the check and Kill. */ }
            catch (Exception cleanup) { cleanupErrors.Add(cleanup); }
            using var closing = new CancellationTokenSource(options.CleanupTimeout);
            try { await process.WaitForExitAsync(closing.Token).ConfigureAwait(false); }
            catch (Exception cleanup) { cleanupErrors.Add(cleanup); }
            try { await all.WaitAsync(closing.Token).ConfigureAwait(false); }
            catch (Exception cleanup)
            {
                if (!all.IsCompleted) cleanupErrors.Add(cleanup);
            }
            // Observe a late failure even when pipe closure outlives the cleanup deadline.
            _ = all.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (cleanupErrors.Count != 0) throw new AggregateException("Process cleanup failed.", new[] { primary }.Concat(cleanupErrors));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }

        async Task<string> CaptureAsync(StreamReader reader)
        {
            try
            {
                var result = new StringBuilder();
                var buffer = new char[4096];
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), operation.Token).ConfigureAwait(false)) != 0)
                {
                    if (result.Length > options.MaximumOutputCharacters - read)
                        throw new InvalidDataException("Process diagnostic output exceeded the character limit.");
                    result.Append(buffer, 0, read);
                }
                return result.ToString();
            }
            catch (Exception error) { fault.TrySetResult(error); throw; }
        }
    }

    // Match CancellationTokenSource's truncation to whole milliseconds, including its upper boundary.
    private static bool IsValidFiniteTimeout(TimeSpan value) =>
        value > TimeSpan.Zero && (long)value.TotalMilliseconds <= uint.MaxValue - 1L;
}
