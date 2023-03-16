using MS.Microservice.Infrastructure.Common.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace MS.Microservice.Infrastructure.Common.Extensions
{
    public static class IApplicationBuilderExtensions
    {
        /// <summary>
        /// 开启请求缓冲倒带
        /// 注意，此方法要在<see cref="EndpointBuilder"/>app.UseEndPoint()之前
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseEnableBuffering(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<EnableBufferingMiddleware>();
        }
    }
}
