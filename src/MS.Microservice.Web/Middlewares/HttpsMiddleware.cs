using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Middlewares
{
    /// <summary>
    /// 用于处理客户IP地址、端口、协议的HostBuilder中间件
    /// </summary>
    public static class HttpsWebHostBuilderExtensions
    {

        /// <summary>
        /// 启用HttpsIntegration中间件
        /// </summary>
        /// <param name="hostBuilder"></param>
        /// <returns></returns>
        public static IWebHostBuilder UseHttpsIntegration(this IWebHostBuilder hostBuilder)
        {
            if (hostBuilder == null)
            {
                throw new ArgumentNullException(nameof(hostBuilder));
            }

            // 检查是否已经加载过了
            if (hostBuilder.GetSetting(nameof(UseHttpsIntegration)) != null)
            {
                return hostBuilder;
            }


            // 设置已加载标记，防止重复加载
            hostBuilder.UseSetting(nameof(UseHttpsIntegration), true.ToString());


            // 添加configure处理
            hostBuilder.ConfigureServices(services =>
             {
                 services.AddSingleton<IStartupFilter>(new HttpsSetupFilter());
             });


            return hostBuilder;
        }

    }

    public class HttpsMiddleware
    {
        readonly RequestDelegate _next;
        public HttpsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var headers = httpContext.Request.Headers;
            try
            {
                foreach (var item in headers)
                {
                    Console.WriteLine($"header: {item.Key}  value: {item.Value}");
                }
                //解析访问者IP地址和端口号
                if (headers != null && headers.ContainsKey("X-Original-For"))
                {
                    var ipaddAdndPort = headers["X-Original-For"].ToArray()[0];
                    var dot = ipaddAdndPort.IndexOf(":");
                    var ip = ipaddAdndPort;
                    var port = 0;
                    if (dot > 0)
                    {
                        ip = ipaddAdndPort.Substring(0, dot);
                        port = int.Parse(ipaddAdndPort.Substring(dot + 1));
                    }

                    httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
                    if (port != 0) httpContext.Connection.RemotePort = port;
                }

                // 下面这段代码用于解决 https 下的 ids容器内还是http的情况，by 微信.龙江  还未验证
                //处理HTTP/HTTPS协议标记
                if (headers != null && headers.ContainsKey("X-Original-Proto"))
                {
                    httpContext.Request.Scheme = headers["X-Original-Proto"].ToArray()[0];
                }
                else if (headers != null && headers.ContainsKey("X-Scheme"))
                {
                    httpContext.Request.Scheme = headers["X-Scheme"].ToArray()[0];
                }
            }
            finally
            {
                await _next(httpContext);
            }
        }
    }


    class HttpsSetupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<HttpsMiddleware>();
                next(app);
            };
        }
    }
}
