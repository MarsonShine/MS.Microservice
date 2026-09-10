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
using MS.Microservice.Web.Application.FeatureManager;
using MS.Microservice.Web.AutofacModules.Extensions;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Web.Infrastructure.Extensions;
using MS.Microservice.Web.Infrastructure.HealthChecks;
using MS.Microservice.Web.Infrastructure.Labs;
using MS.Microservice.Web.Infrastructure.Mediator.Behaviors;
using MS.Microservice.Web.Infrastructure.Mvc.ModelBinder.Extension;
using System.Text.Json;
using Wolverine;

namespace MS.Microservice.Web.Hosting;

/// <summary>
/// Builds the shared platform pipeline. Only a samples host may opt in to lab endpoints.
/// </summary>
public static class PlatformWebHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
        => WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

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

        builder.Host.UseWolverine(options =>
        {
            options.Discovery.IncludeAssembly(typeof(PlatformWebHost).Assembly);
            options.Discovery.IncludeAssembly(typeof(Entity).Assembly);
            options.Discovery.IncludeAssembly(typeof(ActivationDbContext).Assembly);
            options.Policies.AddMiddleware<LoggingMiddleware>();
            options.Policies.AddMiddleware<ValidatorMiddleware>();
            options.Policies.AddMiddleware<InboxConsumptionMiddleware>(chain =>
                typeof(IEventContract).IsAssignableFrom(chain.MessageType));
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
            .AddMvcOptions(options =>
            {
                options.UseApiDecryptModelBinding(builder.Configuration);
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
