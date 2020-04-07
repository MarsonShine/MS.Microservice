using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutoMapper;
using MassTransit;
using MassTransit.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using MS.Microservice.Web;
using MS.Microservice.Web.AutofacModules;
using MS.Microservice.Web.AutoMappers.Profiles;

namespace MS.Microservice
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Latest);
            //integrate autofac
            var builder = new ContainerBuilder();
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
                //options.DescribeAllEnumsAsStrings();
                options.DocInclusionPredicate((docName, description) => true);
            });

            //builder.RegisterModule<ApplicationAutoModule>();  //success
            builder.Populate(services);

            builder.RegisterAssemblyModules(typeof(MediatorModule).Assembly);

            return new AutofacServiceProvider(builder.Build());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

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

            var bus = app.ApplicationServices.GetService<IBusControl>();
            var busHandle = TaskUtil.Await(() => bus.StartAsync());
            lifetime.ApplicationStopping.Register(() => busHandle.Stop());
        }
    }
}
