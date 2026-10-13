using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MS.Microservice.Lab.Application.Tools;

public sealed class ToolDemoSwaggerResponses : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType is not { } controller || !typeof(ToolDemoControllerBase).IsAssignableFrom(controller)) return;
        foreach (var metadata in context.ApiDescription.SupportedResponseTypes)
        {
            if (metadata.Type is null || !typeof(ProblemDetails).IsAssignableFrom(metadata.Type)) continue;
            var status = metadata.StatusCode.ToString(CultureInfo.InvariantCulture);
            if (operation.Responses?.TryGetValue(status, out var value) != true || value is not OpenApiResponse response) continue;
            // ApiExplorer applies the action's file MIME to errors too; ProblemDetails always uses JSON here.
            response.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = context.SchemaGenerator.GenerateSchema(metadata.Type, context.SchemaRepository) }
            };
        }
    }
}

public static class ToolDemoServiceRegistration
{
    public static IServiceCollection AddToolDemoApi(this IServiceCollection services)
    {
        services.AddScoped<ToolDemoFiles>();
        services.AddScoped<ToolDemoExceptionFilter>();
        services.AddSingleton<ToolInferenceRuntime>();
        services.AddSwaggerGen(options => options.OperationFilter<ToolDemoSwaggerResponses>());
        return services;
    }
}
