using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Linq;

namespace MS.Microservice.Infrastructure.Common.Http.Extensions
{
    public static class HttpRequestExtensions
    {
        public static string BearerAuthorization(this HttpRequest request)
        {
            return request.Headers[HeaderNames.Authorization].FirstOrDefault()?.Split(" ").Last();
        }

        public static void EnableBufferingAndSeekBegin(this HttpRequest request)
        {
            request.EnableBuffering();
            request.Body.Seek(0, System.IO.SeekOrigin.Begin);
        }

        public static string GetIP(this HttpContext context)
        {
            return context.Request.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString() + ":" + context.Request.HttpContext.Connection.RemotePort;
        }
    }
}
