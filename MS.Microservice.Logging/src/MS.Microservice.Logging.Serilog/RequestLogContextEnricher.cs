using MS.Microservice.Logging.Core;
using Serilog.Core;
using Serilog.Events;

namespace MS.Microservice.Logging.Serilog;

internal sealed class RequestLogContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var context = RequestLogScope.Current;
        if (context is null)
        {
            return;
        }

        AddPropertyIfValue(logEvent, propertyFactory, RequestLogDefaults.RequestIdPropertyName, context.RequestId);
        AddPropertyIfValue(logEvent, propertyFactory, RequestLogDefaults.PlatformIdPropertyName, context.PlatformId);
        AddPropertyIfValue(logEvent, propertyFactory, RequestLogDefaults.UserFlagPropertyName, context.UserFlag);
        AddPropertyIfValue(logEvent, propertyFactory, RequestLogDefaults.MethodPropertyName, context.Method);
        AddPropertyIfValue(logEvent, propertyFactory, RequestLogDefaults.PathPropertyName, context.Path);

        if (context.StatusCode.HasValue)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(RequestLogDefaults.StatusCodePropertyName, context.StatusCode.Value));
        }

        if (context.ElapsedMilliseconds.HasValue)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(RequestLogDefaults.ElapsedMillisecondsPropertyName, context.ElapsedMilliseconds.Value));
        }
    }

    private static void AddPropertyIfValue(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(propertyName, value));
        }
    }
}