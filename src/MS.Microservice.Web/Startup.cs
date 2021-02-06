using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutoMapper;
using Google.Protobuf.WellKnownTypes;
using MassTransit;
using MassTransit.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using MS.Microservice.Database;
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.Web;
using MS.Microservice.Web.AutofacModules;
using MS.Microservice.Web.AutoMappers.Profiles;
using MS.Microservice.Web.Swagger;
using MySql.Data.EntityFrameworkCore.Extensions;
using Polly;

namespace MS.Microservice
{
    public class Startup
    {
        [MaybeNull]
        public ILifetimeScope AutofacContainer { get; private set; }
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // 定义伸缩性，可用性强的 httpclient
            services.AddHttpClient("highavailable")
                .SetHandlerLifetime(TimeSpan.FromSeconds(10)) // 设置每个请求处理的生存时间（可重用时间）为 10 秒，默认 2 分钟
                .AddTransientHttpErrorPolicy(p => 
                    p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600))
                );

            services.AddEntityFrameworkMySQL()
                .AddDbContext<OrderingContext>(options =>
                {
                    options.UseMySQL(Configuration.GetConnectionString("DefaultConnection"));
                });
            services.AddMvc().SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Latest);

            //integrate automapper
            services.AddAutoMapper(new System.Reflection.Assembly[] { typeof(OrderAutoMapperProfiles).Assembly });

            //integrate swagger
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = SwaggerConsts.DOC_TITLE,
                    Version = SwaggerConsts.DOC_VERSION
                });
                options.ApplyBearerAuthorication();
                //options.DescribeAllEnumsAsStrings();
                options.DocInclusionPredicate((docName, description) => true);
            });
            RegisterEventBus(services);
        }

        private void RegisterEventBus(IServiceCollection services)
        {
            services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
            //services.AddTransient<IEventbus>();
        }

        // ConfigureContainer is where you can register things directly
        // with Autofac. This runs after ConfigureServices so the things
        // here will override registrations made in ConfigureServices.
        // Don't build the container; that gets done for you by the factory.
        public void ConfigureContainer(ContainerBuilder builder)
        {
            // Register your own things directly with Autofac here. Don't
            // call builder.Populate(), that happens in AutofacServiceProviderFactory
            // for you.
            builder.RegisterModule<ApplicationAutoModule>();  //success
            builder.RegisterAssemblyModules(typeof(MediatorModule).Assembly);
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            this.AutofacContainer = app.ApplicationServices.GetAutofacRoot();

            app.Use(async (context, next) =>
            {
                context.Request.Headers.Remove("Connection");
                await next();
            });
            app.UseStaticFiles();
            app.UseRouting();

            app.UseCors();

            app.UseSwagger()
                .UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", SwaggerConsts.API_NAME);
                });

            app.UseEndpoints(cfg =>
            {
                cfg.MapDefaultControllerRoute();
            });

            // 订阅事件
            ConfigureEventBus(app);

            //var bus = app.ApplicationServices.GetService<IBusControl>();
            //var busHandle = TaskUtil.Await(() => bus.StartAsync());
            //lifetime.ApplicationStopping.Register(() => busHandle.Stop());
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IEventbus>();

            //eventBus.Subscribe<ProductPriceChangedIntegrationEvent, ProductPriceChangedIntegrationEventHandler>();
            //eventBus.Subscribe<OrderStartedIntegrationEvent, OrderStartedIntegrationEventHandler>();
        }
    }
}
