using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Linq;

namespace MS.Microservice.Infrastructure.Common.Http.Extensions
{
    public static partial class HttpRequestExtensions
    {
        extension(HttpRequest request)
        {
            public string? BearerAuthorization()
                => request.Headers[HeaderNames.Authorization].FirstOrDefault()?.Split(" ").Last();

            public void EnableBufferingAndSeekBegin()
            {
                request.EnableBuffering();
                request.Body.Seek(0, System.IO.SeekOrigin.Begin);
            }
        }

        extension(HttpContext context)
        {
            public string GetIP()
                => context.Request.HttpContext.Connection.RemoteIpAddress!.MapToIPv4().ToString() + ":" + context.Request.HttpContext.Connection.RemotePort;
        }
    }
}
