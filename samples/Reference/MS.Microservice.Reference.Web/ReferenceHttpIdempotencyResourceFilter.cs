using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MS.Microservice.Idempotency.Mvc;

namespace MS.Microservice.Reference.Web;

public sealed class ReferenceHttpIdempotencyResourceFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        if (!services.GetRequiredService<ReferenceIdempotencyOptions>().Enabled ||
            !http.Request.Headers.ContainsKey(ReferenceHttpIdempotencyExecutor.HeaderName))
        {
            await next();
            return;
        }

        var marker = http.GetEndpoint()?.Metadata
            .GetMetadata<RequireHttpIdempotencyAttribute<ReferenceHttpIdempotencyResourceFilter>>()
            ?? throw new InvalidOperationException("The MVC action is missing its idempotency operation.");
        var invoked = await services.GetRequiredService<ReferenceHttpIdempotencyExecutor>()
            .ExecuteAsync(http, marker.Operation, async () =>
            {
                var executed = await next();
                if (executed.Exception is { } exception && !executed.ExceptionHandled)
                    ExceptionDispatchInfo.Capture(exception).Throw();
            });
        if (!invoked) context.Result = new EmptyResult();
    }
}
