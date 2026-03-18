using Microsoft.AspNetCore.Http;
using NLog;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.LogEnrichers
{
    /// <summary>
    /// NLog 日志事件丰富器：从 HttpContext 中提取请求上下文属性并注入到 NLog LogEventInfo。
    /// 零分配：直接使用 StringValues 索引器访问 header 值，避免 ToString() 分配。
    /// </summary>
    public static class AspNetRequestEnricher
    {
        private const string RequestIdHeader = "requestId";
        private const string PlatformIdHeader = "platformId";
        private const string UserFlagHeader = "userflag";

        /// <summary>
        /// 将 HTTP 请求上下文属性（RequestId, PlatformId, UserFlag）注入到 NLog 日志事件中。
        /// </summary>
        public static void Enrich(LogEventInfo logEvent, HttpContext? httpContext)
        {
            if (httpContext is null) return;

            var headers = httpContext.Request.Headers;

            if (headers.TryGetValue(RequestIdHeader, out var requestId) && requestId.Count > 0)
            {
                logEvent.Properties["AppRequestId"] = requestId[0]!;
            }

            if (headers.TryGetValue(PlatformIdHeader, out var platformId) && platformId.Count > 0)
            {
                logEvent.Properties["PlatformId"] = platformId[0]!;
            }

            if (headers.TryGetValue(UserFlagHeader, out var userFlag) && userFlag.Count > 0)
            {
                logEvent.Properties["UserFlag"] = userFlag[0]!;
            }
        }
    }
}
