using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MS.Microservice.Tasks.Dashboard;

public static partial class TaskDashboardEndpoints
{
    private static void MapAssets(RouteGroupBuilder group, TaskDashboardOptions options)
    {
        MapAsset("/", "index.html", "text/html; charset=utf-8");
        MapAsset("/dashboard.css", "dashboard.css", "text/css; charset=utf-8");
        MapAsset("/dashboard.js", "dashboard.js", "text/javascript; charset=utf-8");

        void MapAsset(string route, string name, string contentType)
        {
            using var input = typeof(TaskDashboardEndpoints).Assembly.GetManifestResourceStream(
                "MS.Microservice.Tasks.Dashboard.Assets." + name)
                ?? throw new InvalidOperationException("A dashboard asset is missing: " + name);
            using var output = new MemoryStream();
            input.CopyTo(output);
            var bytes = output.ToArray();
            var endpoint = group.MapGet(route, (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "private, no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'";
                if (route == "/" && !context.Request.Path.Value!.EndsWith('/'))
                    return Results.Redirect(context.Request.PathBase + context.Request.Path.ToString() + "/" + context.Request.QueryString);
                return Results.Bytes(bytes, contentType);
            });
            if (options.AllowAnonymousShell) endpoint.AllowAnonymous();
            else if (options.AuthorizationPolicy is { } policy) endpoint.RequireAuthorization(policy);
            else endpoint.RequireAuthorization();
        }
    }
}
