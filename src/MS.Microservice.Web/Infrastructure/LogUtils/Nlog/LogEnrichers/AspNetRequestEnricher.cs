using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LogEnrichers
{
    //public class AspNetRequestEnricher : ILogEventEnricher
    //{
    //    private readonly HttpContext _context;
    //    public AspNetRequestEnricher(HttpContext context)
    //    {
    //        _context = context ?? throw new ArgumentNullException(nameof(context));
    //    }

    //    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    //    {
    //        logEvent.AddPropertyIfAbsent(
    //            propertyFactory.CreateProperty("AppRequestId", getHeaders(_context, "RequestId"))
    //            );
    //        logEvent.AddPropertyIfAbsent(
    //            propertyFactory.CreateProperty("PlatformId", getHeaders(_context, "PlatformId"))
    //            );
    //        logEvent.AddPropertyIfAbsent(
    //            propertyFactory.CreateProperty("UserFlag", getHeaders(_context, "UserFlag"))
    //            );

    //        static string getHeaders(HttpContext context, string key)
    //        {
    //            if (context == null) return default;
    //            if (context.Request.Headers.TryGetValue(key, out var val))
    //                return val.ToString();
    //            else
    //                return default;
    //        }
    //    }
    //}
}
