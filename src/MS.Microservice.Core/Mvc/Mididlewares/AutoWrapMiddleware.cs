namespace MS.Microservice.Core.Mvc.Mididlewares
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using MS.Microservice.Core.Dtos;
    using Newtonsoft.Json;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;

    public class AutoWrapMiddleware
    {
        private readonly RequestDelegate _next;

        public AutoWrapMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            var originBody = context.Response.Body;

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;
            await _next(context);

            responseBody.Seek(0, SeekOrigin.Begin);
            using var streamReader = new StreamReader(responseBody);
            var strActionResult = streamReader.ReadToEnd();
            // 还原
            context.Response.Body = originBody;
            if (IsApiRequest(context))
            {
                var objActionResult = JsonConvert.DeserializeObject(strActionResult);
                var responseModel = new ResultDto<object>(objActionResult);

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(responseModel));
            }
            else
            {
                await context.Response.WriteAsync(strActionResult);
            }

        }

        private bool IsApiRequest(HttpContext context)
        {
            var routeData = context.GetRouteData();
            return routeData.Values.TryGetValue("action", out _);
        }
    }
}
