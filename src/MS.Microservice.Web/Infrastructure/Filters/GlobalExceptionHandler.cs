using Microsoft.AspNetCore.Diagnostics;
using MS.Microservice.Domain.Exception;
using MS.Microservice.Web.Infrastructure.Http;
using System.Text.Json;

namespace MS.Microservice.Web.Infrastructure.Filters;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<GlobalExceptionHandler> _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = ApiProblemDetails.FromException(context, exception);
        if (exception is DomainException)
        {
            _logger.LogWarning(exception, "Domain request failed with status {StatusCode}", problem.Status);
        }
        else
        {
            _logger.LogError(exception, "Unhandled request exception");
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            problem,
            SerializerOptions,
            ApiProblemDetails.ContentType,
            cancellationToken);
        return true;
    }
}
