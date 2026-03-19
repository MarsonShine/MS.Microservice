using MS.Microservice.Infrastructure.Common.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace MS.Microservice.Infrastructure.Common.Extensions
{
    public static partial class IApplicationBuilderExtensions
    {
        extension(IApplicationBuilder builder)
        {
            /// <summary>
            /// 开启请求缓冲倒带
            /// 注意，此方法要在<see cref="EndpointBuilder"/>app.UseEndPoint()之前
            /// </summary>
            /// <returns></returns>
            public IApplicationBuilder UseEnableBuffering()
            {
                return builder.UseMiddleware<EnableBufferingMiddleware>();
            }
        }
    }
}
