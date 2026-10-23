using MS.Microservice.Processes;
namespace MS.Microservice.Media.Video;
internal sealed class MediaProcessRunner(TimeSpan timeout)
{
    public async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(executable, arguments, new() { Timeout = timeout }, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new MediaProcessException(executable, result.ExitCode, result.StandardError);
        return result.StandardOutput;
    }
}
