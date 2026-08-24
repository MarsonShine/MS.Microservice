using System.Diagnostics;

namespace MS.Microservice.Infrastructure.Telemetry;

public sealed class PlatformTracing(string activitySourceName) : IDisposable
{
    private readonly ActivitySource _activitySource = new(activitySourceName);

    public Activity? StartActivity(
        string name,
        ActivityKind kind,
        string? traceParent,
        string? traceState)
    {
        if (!string.IsNullOrWhiteSpace(traceParent)
            && ActivityContext.TryParse(traceParent, traceState, out var parentContext))
        {
            return _activitySource.StartActivity(name, kind, parentContext);
        }

        return _activitySource.StartActivity(name, kind);
    }

    public void Dispose() => _activitySource.Dispose();
}
