using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MS.Microservice.Media.Video;

internal sealed class MediaProcessRunner(TimeSpan timeout)
{
    private const int MaximumOutputCharacters = 262_144;

    public async Task<string> RunAsync(
        string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(timeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        if (!process.Start())
            throw new IOException("The media process could not be started.");

        using var cancellation = operation.Token.Register(Terminate);
        var output = CaptureAsync(process.StandardOutput);
        var errors = CaptureAsync(process.StandardError);
        try
        {
            await Task.WhenAll(output, errors, process.WaitForExitAsync(operation.Token)).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            Terminate();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe both readers after terminating the child, preserving the primary failure.
            try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
            catch { }
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
                throw new TimeoutException("The media process exceeded its configured timeout.", error);
            throw;
        }

        if (process.ExitCode != 0)
            throw new MediaProcessException(executable, process.ExitCode, errors.Result);
        return output.Result;

        async Task<string> CaptureAsync(StreamReader reader)
        {
            try
            {
                var result = new StringBuilder();
                var buffer = new char[4096];
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), operation.Token).ConfigureAwait(false)) != 0)
                {
                    if (result.Length > MaximumOutputCharacters - read)
                        throw new InvalidDataException("The media process exceeded its diagnostic output limit.");
                    result.Append(buffer, 0, read);
                }
                return result.ToString();
            }
            catch
            {
                operation.Cancel();
                throw;
            }
        }

        void Terminate()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
}
