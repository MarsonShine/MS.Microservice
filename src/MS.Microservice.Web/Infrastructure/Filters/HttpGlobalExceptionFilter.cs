using MS.Microservice.Core.Dto;
using MS.Microservice.Domain.Exception;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Net;

namespace MS.Microservice.Web.Infrastructure.Filters
{
    public class HttpGlobalExceptionFilter : IExceptionFilter
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HttpGlobalExceptionFilter> _logger;

        public HttpGlobalExceptionFilter(IWebHostEnvironment env, ILogger<HttpGlobalExceptionFilter> logger)
        {
            _env = env;
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            _logger.LogError(new EventId(context.Exception.HResult),
                context.Exception,
                context.Exception.Message);

            if (typeof(ActivationDomainException).IsAssignableFrom(context.Exception.GetType()))
            {
                var exception = context.Exception as ActivationDomainException;
                var result = new ResultDto(false, context.Exception.Message, exception.Code);
                context.Result = new JsonResult(result);
            }
            else
            {
                var result = new ResultDto(false, context.Exception.Message, (int)HttpStatusCode.InternalServerError);
                context.Result = new JsonResult(result);
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
    }
}
