using Microsoft.AspNetCore.Mvc.Filters;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Idempotency.Mvc;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

public sealed class ProfileCreateMvcIdempotencyFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        if (!services.GetRequiredService<ReferenceIdempotencyOptions>().Enabled ||
            !http.Request.Headers.ContainsKey("Idempotency-Key"))
        {
            await next();
            return;
        }

        if (!context.ActionArguments.TryGetValue("request", out var argument) || argument is not CreateProfile request)
            throw new InvalidOperationException("The marked MVC action must have a CreateProfile parameter named request.");

        var identity = services.GetRequiredService<ExternalIdentityOptions>();
        var result = await ProfileIdempotencyHandler.CreateAsync(request,
            ProfileEndpoints.Actor(http.User, identity), services.GetRequiredService<ProfileService>(), http,
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>(),
            services.GetRequiredService<IServiceScopeFactory>(), http.RequestAborted);
        context.Result = new HttpResultActionResult(result);
    }
}
