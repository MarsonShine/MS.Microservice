using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.FeatureManager;
using MS.Microservice.Core.Messaging;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Domain;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Logging.NLog;
using MS.Microservice.Persistence.EFCore.DbContext;
using MS.Microservice.Lab.Application.FeatureManager;
using MS.Microservice.Lab.AutofacModules.Extensions;
using MS.Microservice.Lab.Infrastructure.Cors;
using MS.Microservice.Lab.Infrastructure.Extensions;
using MS.Microservice.Lab.Infrastructure.HealthChecks;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Lab.Infrastructure.Mediator.Behaviors;
using System.Text.Json;
using Wolverine;

namespace MS.Microservice.Lab.Hosting;

/// <summary>
/// Builds the shared platform pipeline. Only a samples host may opt in to lab endpoints.
/// </summary>
public static class PlatformWebHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
        => MS.Microservice.AspNetCore.ServiceHost.CreateBuilder(args);

    public static async Task RunAsync(string[] args, bool enableLabEndpoints)
    {
        var builder = CreateBuilder(args);

        builder.ConfigureMsNLog(options =>
        {
            if (Enum.TryParse<LogLevel>(builder.Configuration["Logging:LogLevel:Default"], true, out var minimumLevel))
            {
                options.MinimumLevel = minimumLevel;
            }
        });

        LabMessaging.Configure(builder, options =>
        {
            options.Discovery.IncludeAssembly(typeof(PlatformWebHost).Assembly);
            options.Discovery.IncludeAssembly(typeof(Entity).Assembly);
            options.Discovery.IncludeAssembly(typeof(ActivationDbContext).Assembly);
            options.Policies.AddMiddleware<LoggingMiddleware>();
            options.Policies.AddMiddleware<ValidatorMiddleware>();
        });

        AddServices(builder, enableLabEndpoints);

        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(containerBuilder =>
            {
                containerBuilder.RegisterPlatformAutofacModule(builder.Configuration);
            });

        var app = builder.Build();
        var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;

        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment() || enableLabEndpoints)
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler();
            app.UseHsts();
        }

        app.UseResponseCompression();
        app.UseMsRequestLogging();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();

        if (corsOptions.IsEnabled)
        {
            app.UseCors(corsOptions.PolicyName);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UsePlatformSwagger();
        app.MapPlatformHealthChecks();
        app.MapControllers();
        LabMessaging.Map(app);

        await app.RunAsync();
    }

    private static void AddServices(WebApplicationBuilder builder, bool enableLabEndpoints)
    {
        builder.Services.AddFeatureToggle(builder.Configuration);
        builder.Services.AddMsRequestLogging();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PlatformWebHost).Assembly)
            .ConfigureApplicationPartManager(options =>
            {
                options.FeatureProviders.Add(
                    new LabOnlyControllerFeatureProvider(enableLabEndpoints));
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Encoder = DefaultSerializeSetting.ChineseEncoder;
            });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        FluentValidation.ValidatorOptions.Global.DefaultRuleLevelCascadeMode =
            FluentValidation.CascadeMode.Stop;

        builder.Services.AddCoreServices(builder.Configuration);
    }
}
