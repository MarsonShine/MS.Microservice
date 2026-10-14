using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MS.Microservice.Tasks.Dashboard;

public static partial class TaskDashboardEndpoints
{
    /// <summary>Registers dashboard services without adding a scheduler, database provider or authentication scheme.</summary>
    public static IServiceCollection AddTaskDashboard(this IServiceCollection services, Action<TaskDashboardOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(entry => entry.ServiceType == typeof(TaskDashboardOptions)))
            throw new InvalidOperationException("Register the task dashboard once per host.");
        var options = new TaskDashboardOptions();
        configure(options);
        ArgumentNullException.ThrowIfNull(options.ResolveScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        if (options.Title.Length > 100) throw new ArgumentOutOfRangeException(nameof(configure));
        services.AddSingleton(options);
        services.AddSingleton<CoravelScheduleCatalog>();
        services.AddAntiforgery();
        return services;
    }

    /// <summary>Mounts dashboard endpoints with host authentication and trusted scope resolution.</summary>
    public static RouteGroupBuilder MapTaskDashboard(this IEndpointRouteBuilder endpoints, string path = "/task-center")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path == "/"
            || path.IndexOfAny(['?', '#', '{', '}', '\\']) >= 0 || path.EndsWith('/'))
            throw new ArgumentException("Use a literal path such as /task-center without a trailing slash.", nameof(path));
        var options = endpoints.ServiceProvider.GetRequiredService<TaskDashboardOptions>();
        var group = endpoints.MapGroup(path).ExcludeFromDescription();
        var api = group.MapGroup("/api");
        Authorize(api, options.AuthorizationPolicy);
        api.AddEndpointFilter(async (context, next) =>
        {
            var scope = options.ResolveScope(context.HttpContext);
            if (string.IsNullOrWhiteSpace(scope)) return Results.Forbid();
            context.HttpContext.Items[typeof(TaskDashboardOptions)] = scope;
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        api.MapGet("/session", async (HttpContext context, IAntiforgery antiforgery,
            IOptions<AntiforgeryOptions> antiforgeryOptions, IAuthorizationService authorization) =>
        {
            var header = antiforgeryOptions.Value.HeaderName;
            if (string.IsNullOrEmpty(header)) throw new InvalidOperationException("Dashboard controls require an antiforgery header name.");
            var tokens = antiforgery.GetAndStoreTokens(context);
            var canControl = !options.ReadOnly && await Allowed(authorization, context, options.ControlAuthorizationPolicy);
            var canViewSchedules = await Allowed(authorization, context, options.SchedulesAuthorizationPolicy);
            return Results.Ok(new { options.Title, CanControl = canControl, CanViewSchedules = canViewSchedules,
                CsrfHeaderName = header, CsrfRequestToken = tokens.RequestToken });
        });
        api.MapGet("/jobs", async (HttpContext context, IJobStore store, JobState? state, int? take) =>
        {
            var count = take ?? 100;
            if (count is < 1 or > 200 || state is { } value && !Enum.IsDefined(value))
                return Results.BadRequest(new { detail = "take must be 1–200 and state must be a known job state." });
            var jobs = await store.ListAsync(Scope(context), state, count + 1, context.RequestAborted);
            return Results.Ok(new { Items = jobs.Take(count).Select(DashboardJob.From), HasMore = jobs.Count > count });
        });
        api.MapGet("/jobs/{jobId:guid}", async (HttpContext context, IJobStore store, Guid jobId) =>
        {
            if (jobId == Guid.Empty) return Results.NotFound();
            var job = await store.GetAsync(Scope(context), jobId, context.RequestAborted);
            return job is null ? Results.NotFound() : Results.Ok(DashboardJob.From(job));
        });
        api.MapGet("/jobs/{jobId:guid}/attempts", async (HttpContext context, IJobStore store, Guid jobId) =>
        {
            if (jobId == Guid.Empty || await store.GetAsync(Scope(context), jobId, context.RequestAborted) is null)
                return Results.NotFound();
            return Results.Ok(await store.GetAttemptsAsync(Scope(context), jobId, context.RequestAborted));
        });
        var schedules = api.MapGet("/schedules", (CoravelScheduleCatalog catalog) => Results.Ok(catalog.GetSchedules()));
        if (options.SchedulesAuthorizationPolicy is { } schedulesPolicy)
            schedules.RequireAuthorization(schedulesPolicy);
        var controls = api.MapGroup("/jobs/{jobId:guid}");
        if (options.ControlAuthorizationPolicy is { } controlPolicy) controls.RequireAuthorization(controlPolicy);
        controls.AddEndpointFilter(async (context, next) =>
        {
            if (options.ReadOnly) return Results.Forbid();
            try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { detail = "页面验证已失效，请重新连接后重试。" }); }
            return await next(context);
        });
        controls.MapPost("/cancel", async (HttpContext context, IJobStore store, Guid jobId) =>
            jobId == Guid.Empty ? Results.NotFound() : Updated(await store.CancelAsync(Scope(context), jobId, context.RequestAborted)));
        controls.MapPost("/retry", async (HttpContext context, IJobStore store, Guid jobId) =>
            jobId == Guid.Empty ? Results.NotFound() : Updated(await store.RetryAsync(Scope(context), jobId, cancellationToken: context.RequestAborted)));
        return group;
    }

    private static string Scope(HttpContext context) => (string)context.Items[typeof(TaskDashboardOptions)]!;
    private static IResult Updated(JobUpdateResult result) => result switch
    {
        JobUpdateResult.Applied => Results.NoContent(), JobUpdateResult.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { detail = "当前状态或剩余执行次数不允许此操作。" })
    };
    private static void Authorize(RouteGroupBuilder group, string? policy)
    {
        if (policy is null) group.RequireAuthorization(); else group.RequireAuthorization(policy);
    }
    private static async Task<bool> Allowed(IAuthorizationService authorization, HttpContext context, string? policy)
        => policy is null || (await authorization.AuthorizeAsync(context.User, null, policy)).Succeeded;
}
