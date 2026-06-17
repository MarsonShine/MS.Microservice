using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Logging.NLog;
using MS.Microservice.Web.Infrastructure.Extensions;
using MS.Microservice.Web.Infrastructure.Mvc.ModelBinder.Extension;
using System.Text.Json;
using Autofac;
using MS.Microservice.Web.AutofacModules.Extensions;
using MS.Microservice.Web.Infrastructure.Cors;
using Microsoft.AspNetCore.ResponseCompression;
using MS.Microservice.Core.FeatureManager;
using MS.Microservice.Web.Application.FeatureManager;
using Wolverine;
using MS.Microservice.Web.Infrastructure.Mediator.Behaviors;
using MS.Microservice.Core.Serialization;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.ConfigureMsNLog(options =>
        {
            if (Enum.TryParse<LogLevel>(builder.Configuration["Logging:LogLevel:Default"], true, out var minimumLevel))
            {
                options.MinimumLevel = minimumLevel;
            }
        });

        // Configure Wolverine
        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
            opts.Discovery.IncludeAssembly(typeof(Entity).Assembly);
            opts.Discovery.IncludeAssembly(typeof(ActivationDbContext).Assembly);
            opts.Policies.AddMiddleware<LoggingMiddleware>();
            opts.Policies.AddMiddleware<ValidatorMiddleware>();
        });

        AddCollectionService(builder);

        void AddCollectionService(WebApplicationBuilder builder)
        {
            builder.Services.AddFeatureToggle(builder.Configuration);
            builder.Services.AddMsRequestLogging();
            builder.Services.AddControllers()
            .AddMvcOptions(options =>
            {
                //options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                options.UseApiDecryptModelBinding(builder.Configuration);
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Encoder = DefaultSerializeSetting.ChineseEncoder;
                // options.JsonSerializerOptions.Converters.Add(new MyCustomJsonConverter());
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
            // FluentValidation global validation mode
            FluentValidation.ValidatorOptions.Global.DefaultRuleLevelCascadeMode = FluentValidation.CascadeMode.Stop;

            builder.Services.AddCoreServices(builder.Configuration);
        }

        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureContainer<ContainerBuilder>(containerBuilder =>
            {
                containerBuilder.RegisterPlatformAutofacModule(builder.Configuration);
            });

        var app = builder.Build();
        var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;

        // Configure middleware pipeline
        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseResponseCompression();
        app.UseMsRequestLogging();

        // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-6.0#serve-default-documents
        app.UseDefaultFiles();
        app.UseStaticFiles();
        //app.UsePathBase(new Microsoft.AspNetCore.Http.PathString());

        app.UseRouting();

        if (corsOptions.IsEnabled)
        {
            app.UseCors(corsOptions.PolicyName);
        }

        #region Authorization
        app.UseAuthentication();
        //app.UseMiddleware<ValidateTokenMiddleware>();
        app.UseAuthorization();
        #endregion

        app.UsePlatformSwagger();

        app.MapHealthChecks("/hc");
        app.MapControllers();

        app.Run();
    }
}

#region Authorization

#endregion
