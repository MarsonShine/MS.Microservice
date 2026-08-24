using System.Diagnostics;
using MS.Microservice.Infrastructure.Telemetry;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Telemetry;

public sealed class PlatformTracingTests
{
    [Fact]
    public void StartActivity_WithW3CParent_ContinuesOriginalTrace()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "tests.messaging",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var tracing = new PlatformTracing("tests.messaging");
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        using var activity = tracing.StartActivity(
            "messaging.consume",
            ActivityKind.Consumer,
            traceParent,
            "vendor=value");

        Assert.NotNull(activity);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", activity!.TraceId.ToString());
        Assert.Equal("00f067aa0ba902b7", activity.ParentSpanId.ToString());
        Assert.Equal("vendor=value", activity.TraceStateString);
    }
}
