using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace MS.Microservice.Documents.Rendering;

internal static class RenderWorkerSupervisor
{
    internal static Task<(byte[] Pdf, int Pages)> RunAsync(HtmlWorkerRequest request, CancellationToken cancellationToken) =>
        RunCoreAsync(request, request.Browser.DotNetHostPath, request.Browser.TemporaryDirectory, "--html-worker", request.Options.Timeout,
            request.Options.Limits.MaximumPdfBytes, request.Options.Limits.MaximumPages, cancellationToken);

    internal static Task<(byte[] Pdf, int Pages)> RunAsync(WordWorkerRequest request, CancellationToken cancellationToken) =>
        RunCoreAsync(request, request.Office.DotNetHostPath, request.Office.TemporaryDirectory, "--word-worker", request.Options.Timeout,
            request.Options.Limits.MaximumPdfBytes, request.Options.Limits.MaximumPages, cancellationToken);

    private static async Task<(byte[] Pdf, int Pages)> RunCoreAsync<TRequest>(TRequest request, string dotnetHost, string temporaryDirectory,
        string mode, TimeSpan processingTimeout, int maximumPdfBytes, int maximumPages, CancellationToken cancellationToken)
    {
        var label = mode == "--html-worker" ? "HTML" : "DOCX";
        var assembly = typeof(HtmlPdfRenderer).Assembly.Location;
        if (!RuntimeFeature.IsDynamicCodeSupported || string.IsNullOrEmpty(assembly)
            || !File.Exists(Path.ChangeExtension(assembly, ".runtimeconfig.json")) || !File.Exists(Path.ChangeExtension(assembly, ".deps.json")))
            throw new NotSupportedException("Deploy the Rendering assembly with its runtimeconfig/deps and Playwright driver files; AOT, trimming and single-file deployments are unsupported.");
        var host = ResolveHostMetadata(AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string);
        var job = Path.Combine(temporaryDirectory, (mode == "--html-worker" ? "ms-html-" : "ms-word-") + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(processingTimeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process { StartInfo = StartInfo(dotnetHost, assembly, job, mode, host.RuntimeConfig, host.Dependencies) };
        var diagnostics = new StringBuilder();
        var cleanupErrors = new List<Exception>();
        WindowsProcessJob? processJob = null;
        Exception? failure = null;
        byte[]? wire = null;
        Task? all = null;
        var started = false;
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(job);
            started = process.Start();
            if (!started) throw new InvalidOperationException($"The {label} worker could not be started.");
            // The worker cannot spawn native children until its complete stdin request arrives.
            processJob = new WindowsProcessJob(process);
            var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Observe(Task task)
            {
                try { await task.ConfigureAwait(false); }
                catch (Exception error) { fault.TrySetResult(error); throw; }
            }
            var reading = ReadOutputAsync(process.StandardOutput.BaseStream, maximumPdfBytes + 4, label, stop.Token);
            all = Task.WhenAll(Observe(SendAsync(process, request, stop.Token)), Observe(reading),
                Observe(ReadDiagnosticsAsync(process.StandardError, diagnostics, stop.Token)), Observe(process.WaitForExitAsync(stop.Token)));
            if (await Task.WhenAny(all, fault.Task).ConfigureAwait(false) == fault.Task)
                ExceptionDispatchInfo.Capture(await fault.Task.ConfigureAwait(false)).Throw();
            await all.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var message = $"{label} worker exited with code {process.ExitCode}: {diagnostics}";
                if (process.ExitCode == 3) throw new InvalidDataException(message);
                throw new InvalidOperationException(message);
            }
            wire = await reading.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = cancellationToken.IsCancellationRequested ? new OperationCanceledException($"{label} rendering was canceled.", error, cancellationToken)
                : timeout.IsCancellationRequested ? new TimeoutException($"{label} rendering exceeded its timeout.", error) : error;
        }
        finally
        {
            stop.Cancel();
            using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (started)
            {
                try
                {
                    if (processJob is not null && processJob.ActiveProcesses != 0) processJob.Terminate();
                    else if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(closing.Token).ConfigureAwait(false);
                    if (processJob is not null) await processJob.WaitForEmptyAsync(closing.Token).ConfigureAwait(false);
                }
                catch (Exception error) { cleanupErrors.Add(error); }
            }
            if (all is not null)
            {
                try { await all.ConfigureAwait(false); }
                catch when (failure is not null) { /* The first pipeline failure is preserved above. */ }
            }
            processJob?.Dispose();
            if ((!started || process.HasExited) && cleanupErrors.Count == 0 && Directory.Exists(job))
            {
                try { await DeleteOwnedDirectoryAsync(temporaryDirectory, job, closing.Token).ConfigureAwait(false); }
                catch (Exception error) { cleanupErrors.Add(error); }
            }
        }
        if (cleanupErrors.Count != 0)
            throw new AggregateException($"{label} worker cleanup failed; retained directory: {job}", failure is null ? cleanupErrors : [failure, .. cleanupErrors]);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (wire is null || wire.Length < 5) throw new InvalidDataException($"The {label} worker returned an incomplete result.");
        var pages = BinaryPrimitives.ReadInt32LittleEndian(wire);
        if (pages < 1 || pages > maximumPages) throw new InvalidDataException($"The {label} worker returned an invalid page count.");
        return (wire[4..], pages);
    }

    internal static (string RuntimeConfig, string Dependencies) ResolveHostMetadata(string? dependencyFiles)
    {
        // The runtime exposes application deps first, followed by shared-framework deps.
        var dependencies = dependencyFiles?.Split(';', 2)[0];
        const string suffix = ".deps.json";
        if (string.IsNullOrEmpty(dependencies) || !Path.IsPathFullyQualified(dependencies)
            || !dependencies.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Rendering requires a standard consumer deployment with application dependency metadata.");
        var runtimeConfig = dependencies[..^suffix.Length] + ".runtimeconfig.json";
        if (!File.Exists(dependencies) || !File.Exists(runtimeConfig))
            throw new NotSupportedException("Deploy the consumer's .deps.json and matching .runtimeconfig.json beside its application files.");
        return (runtimeConfig, dependencies);
    }

    private static ProcessStartInfo StartInfo(string dotnetHost, string assembly, string job, string mode, string runtimeConfig, string dependencies)
    {
        var info = new ProcessStartInfo(dotnetHost)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = job
        };
        // Reuse the consumer's resolved package versions and frameworks, while executing only Rendering.Main.
        foreach (var argument in new[] { "exec", "--runtimeconfig", runtimeConfig,
            "--depsfile", dependencies, assembly, mode }) info.ArgumentList.Add(argument);
        foreach (var key in new[] { "TEMP", "TMP", "TMPDIR" }) info.Environment[key] = job;
        foreach (var key in new[] { "PLAYWRIGHT_DRIVER_SEARCH_PATH", "PLAYWRIGHT_NODEJS_PATH", "PWDEBUG", "NODE_OPTIONS", "DEBUG" }) info.Environment.Remove(key);
        return info;
    }

    private static async Task SendAsync<TRequest>(Process process, TRequest request, CancellationToken cancellationToken)
    {
        try { await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, cancellationToken: cancellationToken).ConfigureAwait(false); }
        finally { process.StandardInput.Close(); }
    }

    private static async Task<byte[]> ReadOutputAsync(Stream stream, int maximum, string label, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(81920, maximum)];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, (long)maximum - output.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (read > maximum - output.Length) throw new InvalidDataException($"The {label} worker output exceeds the byte limit.");
            var required = (int)output.Length + read;
            if (required > output.Capacity) output.Capacity = (int)Math.Min(maximum, Math.Max(required, Math.Max(256L, output.Capacity * 2L)));
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    internal static async Task ReadDiagnosticsAsync(StreamReader stream, StringBuilder target, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            target.Append(buffer, 0, Math.Min(read, 16_384 - target.Length));
    }

    internal static async Task DeleteOwnedDirectoryAsync(string parent, string directory, CancellationToken cancellationToken)
    {
        var parentPath = Path.GetFullPath(parent);
        var root = Path.EndsInDirectorySeparator(parentPath) ? parentPath : parentPath + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (Path.TrimEndingDirectorySeparator(target).Equals(Path.TrimEndingDirectorySeparator(parentPath), StringComparison.OrdinalIgnoreCase)
            || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cleanup must stay inside the owned temporary root.", nameof(directory));
        while (Directory.Exists(target))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { Directory.Delete(target, recursive: true); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
