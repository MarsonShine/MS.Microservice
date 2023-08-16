using Autofac;
using MS.Microservice.Infrastructure.Common.Extensions;
using MS.Microservice.Web.AutofacModules.Extensions;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Web.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Web.Infrastructure.Filters;
using MS.Microservice.Web.Infrastructure.Mvc.ModelBinder.Extension;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MS.Microservice.Web.Application.BackgroundServices;

namespace MS.Microservice.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddMvcOptions(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                options.UseApiDecryptModelBinding(Configuration);
            }).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                // 这里添加自定义json转换器
                // options.JsonSerializerOptions.Converters.Add(new MyCustomJsonConverter());
            });

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });
            // .. 这里可以添加想要的 service
            // 设置全局 fluentvalidation 的校验模式
            FluentValidation.ValidatorOptions.Global.DefaultRuleLevelCascadeMode = FluentValidation.CascadeMode.Stop;
            services.AddFzPlatformServices(Configuration)
            ;
            // 注册后台作业
            services.AddHostedService<HighPerformanceBackgroundService>();
            // 健康检查
#if RELEASE

#endif
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            // Register your own things directly with Autofac here. Don't
            // call builder.Populate(), that happens in AutofacServiceProviderFactory
            // for you.
            builder.RegisterPlatformAutofacModule(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseForwardedHeaders();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }
            app.UseCors(Configuration.GetSection("CorsOptions").Get<CorsOptions>()!.PolicyName);

            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-6.0#serve-default-documents
            // 顺序不能错
            app.UseDefaultFiles();
            app.UseStaticFiles();
            //app.UsePathBase(new Microsoft.AspNetCore.Http.PathString());

            app.UseRouting();

            ConfigureAuth(app);
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.OAuthClientId("activationswaggerui");
                c.OAuthAppName("ActivationSystem Swagger UI");
            });

            app.UseEnableBuffering();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
                endpoints.MapControllers();

                endpoints.MapHealthChecks("/hc");
            });
        }

        private static void ConfigureAuth(IApplicationBuilder app)
        {
            app.UseAuthentication();
            //app.UseMiddleware<ValidateTokenMiddleware>();
            app.UseAuthorization();
        }
    }
}
