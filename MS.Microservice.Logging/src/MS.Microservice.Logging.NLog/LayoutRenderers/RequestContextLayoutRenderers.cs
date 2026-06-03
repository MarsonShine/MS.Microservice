using System.Text;
using MS.Microservice.Logging.Core;
using NLog;
using NLog.LayoutRenderers;

namespace MS.Microservice.Logging.NLog.LayoutRenderers;

[LayoutRenderer("RequestDuration")]
public sealed class RequestDurationLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (RequestLogScope.Current?.ElapsedMilliseconds is long elapsedMilliseconds)
        {
            builder.Append(elapsedMilliseconds);
            builder.Append("ms");
        }
    }
}

[LayoutRenderer("hours")]
public sealed class HoursLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent) =>
        builder.Append(logEvent.TimeStamp.Hour);
}

[LayoutRenderer("year")]
public sealed class YearLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent) =>
        builder.Append(logEvent.TimeStamp.Year);
}

[LayoutRenderer("month")]
public sealed class MonthLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent) =>
        builder.Append(logEvent.TimeStamp.Month);
}

[LayoutRenderer("NetAddress")]
public sealed class NetAddressLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (!string.IsNullOrWhiteSpace(NLogProviderState.NetworkAddress))
        {
            builder.Append(NLogProviderState.NetworkAddress);
        }
    }
}

[LayoutRenderer("requestId")]
public sealed class RequestIdLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (!string.IsNullOrWhiteSpace(RequestLogScope.Current?.RequestId))
        {
            builder.Append(RequestLogScope.Current!.RequestId);
        }
    }
}

[LayoutRenderer("platformId")]
public sealed class PlatformIdLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (!string.IsNullOrWhiteSpace(RequestLogScope.Current?.PlatformId))
        {
            builder.Append(RequestLogScope.Current!.PlatformId);
        }
    }
}

[LayoutRenderer("userflag")]
public sealed class UserFlagLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (!string.IsNullOrWhiteSpace(RequestLogScope.Current?.UserFlag))
        {
            builder.Append(RequestLogScope.Current!.UserFlag);
        }
    }
}