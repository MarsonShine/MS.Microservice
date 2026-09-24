using Microsoft.AspNetCore.Builder;

namespace MS.Microservice.Reference.Web;

internal static class ProfileIdempotencyEndpointExtensions
{
    internal static RouteHandlerBuilder RequireHttpIdempotency(this RouteHandlerBuilder route, string operation)
    {
        route.Finally(endpoint =>
        {
            if (!endpoint.ApplicationServices.GetRequiredService<ReferenceIdempotencyOptions>().Enabled) return;
            var next = endpoint.RequestDelegate ?? throw new InvalidOperationException("The endpoint has no request delegate.");
            endpoint.RequestDelegate = async http =>
            {
                if (!http.Request.Headers.ContainsKey(ReferenceHttpIdempotencyExecutor.HeaderName))
                {
                    await next(http);
                    return;
                }
                await http.RequestServices.GetRequiredService<ReferenceHttpIdempotencyExecutor>()
                    .ExecuteAsync(http, operation, () => next(http));
            };
        });
        return route;
    }
}
