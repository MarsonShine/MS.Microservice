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

        // The request field names are distinct, so an initially empty event cannot have a collision.
        var hasEventProperties = logEvent.Properties.Count != 0;
        SetString(RequestLogDefaults.RequestIdPropertyName, context.RequestId);
        SetString(RequestLogDefaults.PlatformIdPropertyName, context.PlatformId);
        SetString(RequestLogDefaults.UserFlagPropertyName, context.UserFlag);
        SetString(RequestLogDefaults.MethodPropertyName, context.Method);
        SetString(RequestLogDefaults.PathPropertyName, context.Path);

        if (context.StatusCode is int statusCode)
        {
            SetProperty(RequestLogDefaults.StatusCodePropertyName, statusCode);
        }

        if (context.ElapsedMilliseconds is long elapsed)
        {
            SetProperty(RequestLogDefaults.ElapsedMillisecondsPropertyName, elapsed);
        }

        void SetString(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                SetProperty(name, value);
            }
        }

        void SetProperty(string name, object value)
        {
            if (hasEventProperties)
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, value));
            }
            else
            {
                logEvent.AddPropertyIfAbsent(propertyFactory, name, value);
            }
        }
    }
}
