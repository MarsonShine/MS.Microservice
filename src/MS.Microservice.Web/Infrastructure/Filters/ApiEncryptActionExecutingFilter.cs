using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Infrastructure.Attributes;
using MS.Microservice.Infrastructure.Common.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Filters
{
    public class ApiEncryptActionExecutingFilter : ActionFilterAttribute
    {
        private readonly IConfiguration _configuration;
        public ApiEncryptActionExecutingFilter(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;
            if (context.ActionDescriptor.EndpointMetadata
                .Any(attribute => attribute.GetType() == typeof(NoEncryptAttribute)))
            {
                await base.OnActionExecutionAsync(context, next);
                return;
            }

            var checkSwitch = _configuration["ApiEncryptOptions:ApiEncryptSwitch"]; ;
            if (checkSwitch == "Enabled")
            {

                string? content = null;
                if (request.Method.ToLower() == "post" && request.ContentType == "application/json")
                {
                    request.EnableBufferingAndSeekBegin();
                    using StreamReader reader = new(request.Body);
                    content = await reader.ReadToEndAsync();

                }
                else if (request.Method.ToLower() == "get")
                {
                    content = request.QueryString.Value;
                }


                if (content?.Length > 0)
                {
                    var sign = request.Headers["sign"].FirstOrDefault()?.Split(" ").Last();
                    var conSha = CryptologyHelper.HmacSha256(content);
                    if (conSha != sign)
                    {
                        //错误的请求
                        var result = new ResultDto(false, "签名参数错误", (int)HttpStatusCode.OK);
                        context.Result = new JsonResult(result);
                        return;
                    }
                }
            }


            await base.OnActionExecutionAsync(context, next);
        }
    }
}
