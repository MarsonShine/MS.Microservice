using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using MS.Microservice.Web.Infrastructure.Extensions;
using MS.Microservice.Web.Infrastructure.Mvc.ModelBinder.Extension;
using System.Text.Encodings.Web;
using System.Text.Json;
using Autofac;
using MS.Microservice.Web.AutofacModules.Extensions;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Infrastructure.Common.Extensions;
using Microsoft.AspNetCore.ResponseCompression;
using MS.Microservice.Core.FeatureManager;
using MS.Microservice.Swagger.Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog;
using Wolverine;
using MS.Microservice.Web.Infrastructure.Mediator.Behaviors;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.ConfigurePlatformLogging(cfg =>
        {
            cfg.NLogConfiguration();
        });

        // Configure Wolverine
        builder.Host.UseWolverine(opts =>
        {
            // Auto-discover handlers from the Web assembly
            opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
            opts.Policies.AddMiddleware<LoggingMiddleware>();
            opts.Policies.AddMiddleware<ValidatorMiddleware>();
            // Wolverine will automatically discover and wire up handlers
            // Middleware can be applied using attributes or custom conventions
        });

        AddCollectionService(builder);

        void AddCollectionService(WebApplicationBuilder builder)
        {
            builder.Services.AddFeatureToggle(builder.Configuration);
            builder.Services.AddControllers()
            .AddMvcOptions(options =>
            {
                //options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                options.UseApiDecryptModelBinding(builder.Configuration);
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.CjkUnifiedIdeographs, UnicodeRanges.CjkSymbolsandPunctuation, UnicodeRanges.HangulSyllables);
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
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

        app.UseRouting();

        app.UseCors(builder.Configuration.GetSection("CorsOptions").Get<CorsOptions>()!.PolicyName);

        #region Authorization
        app.UseAuthentication();
        //app.UseMiddleware<ValidateTokenMiddleware>();
        app.UseAuthorization();
        #endregion

        app.UsePlatformSwagger();

        // Configure middleware pipeline
        app.UseForwardedHeaders();
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-6.0#serve-default-documents
        app.UseDefaultFiles();
        app.UseStaticFiles();
        //app.UsePathBase(new Microsoft.AspNetCore.Http.PathString());

        app.MapControllers();
        app.MapHealthChecks("/hc");
        app.UseEnableBuffering();

        app.UseResponseCompression();
        app.UsePlatformLogger();

        app.Run();
    }
}

#region Authorization

#endregion
