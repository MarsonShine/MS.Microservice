using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Dto;
using MS.Microservice.Domain.Exception;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Filters
{
	/// <summary>
	/// https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.iexceptionhandler?view=aspnetcore-8.0
	/// </summary>
	/// <param name="logger"></param>
	public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
	{
		private readonly ILogger<GlobalExceptionHandler> _logger = logger;
		public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
		{
			_logger.LogError(exception, exception.Message);
			if (exception is DomainException domainException)
			{
				var result = new ResultDto(false, domainException.Message, domainException!.Code);
				await context.Response.WriteAsJsonAsync(result, cancellationToken);
			}
			else
			{
				var result = new ResultDto(false, exception.Message, (int)HttpStatusCode.InternalServerError);
				await context.Response.WriteAsJsonAsync(result, cancellationToken);
				context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
			}
			return true;
		}
	}
}
