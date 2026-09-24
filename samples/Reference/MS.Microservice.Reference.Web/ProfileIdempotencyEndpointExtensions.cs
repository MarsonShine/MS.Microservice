using Microsoft.AspNetCore.Builder;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal static class ProfileIdempotencyEndpointExtensions
{
    internal static RouteHandlerBuilder RequireHttpIdempotency(this RouteHandlerBuilder route)
    {
        return route.AddEndpointFilterFactory(static (factory, next) =>
        {
            if (!factory.ApplicationServices.GetRequiredService<ReferenceIdempotencyOptions>().Enabled)
                return next;

            return async invocation =>
            {
                var http = invocation.HttpContext;
                if (!http.Request.Headers.ContainsKey("Idempotency-Key"))
                    return await next(invocation);

                var request = invocation.GetArgument<CreateProfile>(0);
                var service = invocation.GetArgument<ProfileService>(1);
                var identity = invocation.GetArgument<ExternalIdentityOptions>(3);
                var services = http.RequestServices;
                return await ProfileIdempotencyHandler.CreateAsync(request,
                    ProfileEndpoints.Actor(http.User, identity),
                    service, http, services.GetRequiredService<IUnitOfWork>(),
                    services.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>(),
                    services.GetRequiredService<IServiceScopeFactory>(), http.RequestAborted);
            };
        });
    }
}
