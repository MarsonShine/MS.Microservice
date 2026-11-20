using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.Core.Auditing;

namespace MS.Microservice.AspNetCore.Auditing;

public sealed record AuditHttpIdentity(string Scope, string Actor);
public sealed class AuditHttpOptions
{
    /// <summary>Resolves identity from host authentication; returning null denies an audited action.</summary>
    public Func<HttpContext, AuditHttpIdentity?> ResolveIdentity { get; set; } = null!;
}

public static class AuditOperationMvcExtensions
{
    /// <summary>Creates a shared audit operation for tagged MVC actions without recording request bodies or results.</summary>
    public static IMvcBuilder AddAutomaticAuditOperations(this IMvcBuilder builder, Action<AuditHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AuditHttpOptions(); configure(options);
        ArgumentNullException.ThrowIfNull(options.ResolveIdentity);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddScoped<AuditOperationContext>();
        builder.Services.TryAddSingleton<AuditOperationFilter>();
        builder.AddMvcOptions(mvc => mvc.Filters.AddService<AuditOperationFilter>());
        return builder;
    }
}

internal sealed class AuditOperationFilter(AuditHttpOptions options)
    : IAsyncActionFilter, IOrderedFilter
{
    public int Order => int.MinValue + 100;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var marker = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<AuditOperationAttribute>();
        if (marker is null) { await next(); return; }
        var identity = options.ResolveIdentity(context.HttpContext);
        if (identity is null)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "缺少可信审计身份。" }) { StatusCode = 403 };
            return;
        }
        using var scope = context.HttpContext.RequestServices.GetRequiredService<AuditOperationContext>().Begin(identity.Scope, identity.Actor, marker.Name,
            correlationId: Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
        await next();
    }
}
