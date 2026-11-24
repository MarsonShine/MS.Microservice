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
        var address = NLogProviderState.NetworkAddress;
        if (!string.IsNullOrWhiteSpace(address))
        {
            builder.Append(address);
        }
    }
}

[LayoutRenderer("requestId")]
public sealed class RequestIdLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        var requestId = RequestLogScope.Current?.RequestId;
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            builder.Append(requestId);
        }
    }
}

[LayoutRenderer("platformId")]
public sealed class PlatformIdLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        var platformId = RequestLogScope.Current?.PlatformId;
        if (!string.IsNullOrWhiteSpace(platformId))
        {
            builder.Append(platformId);
        }
    }
}

[LayoutRenderer("userflag")]
public sealed class UserFlagLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        var userFlag = RequestLogScope.Current?.UserFlag;
        if (!string.IsNullOrWhiteSpace(userFlag))
        {
            builder.Append(userFlag);
        }
    }
}
