using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Text;
using IdentityModel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extension.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.Core.Ceching;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Infrastructure.HealthChecks;
using MS.Microservice.Infrastructure.SqlSugar;
using MS.Microservice.Swagger.Swagger;
using MS.Microservice.Web.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Web.Infrastructure.Filters;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog;

namespace MS.Microservice.Web.Infrastructure.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, [NotNull] IConfiguration configuration)
        {
            services.AddMSLoggerService().WithNLogger(cfg =>
            {
                // 可从配置读取最小级别等，也可以保持默认
                cfg.LogLevel = configuration["Logging:LogLevel:Default"] ?? cfg.LogLevel;
            }); // NLog日志

            services
                .AddCustomMvc(configuration)
                .AddHealthChecks(configuration)
                .AddMySql(configuration)
                .AddCustomSwagger(configuration)
                .AddCustomConfiguration(configuration)
                .AddCustomAuthentication(configuration)
                ;

            return services;
        }

        public static IServiceCollection AddCustomMvc(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddControllers(options =>
            {
                //options.Filters.Add(typeof(HttpGlobalExceptionFilter));
            }).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.WriteIndented = true;
                // 这里添加自定义json转换器
                // options.JsonSerializerOptions.Converters.Add(new MyCustomJsonConverter());
            });

            services.AddCorsService(option =>
            {
                var cors = configuration.GetSection("CorsOptions").Get<CorsOptions>() ?? throw new ArgumentException(nameof(CorsOptions));
                option.IsEnabled = cors.IsEnabled;
                option.PolicyName = cors.PolicyName;
                option.Origins = cors.Origins;
                option.IsAllCors = cors.IsAllCors;
            });

#if NET9_0_OR_GREATER
            services.AddHybridCache();
#else
            services.AddMemoryCache(options =>
            {
                var cacheOptions = configuration.GetSection("CacheOptions").Get<CacheOptions>() ?? throw new ArgumentException(nameof(CacheOptions));
                options.ExpirationScanFrequency = System.TimeSpan.FromSeconds(cacheOptions.SlidingExpirationSecond);
            }).AddDistributedMemoryCache(options =>
                {
                    var cacheOptions = configuration.GetSection("CacheOptions").Get<CacheOptions>() ?? throw new ArgumentException(nameof(CacheOptions));
                    options.ExpirationScanFrequency = System.TimeSpan.FromSeconds(cacheOptions.SlidingExpirationSecond);
                });
#endif

            services.AddHttpClient<LogHttpClient>();

            // 异常处理，可以管道化
            services.AddExceptionHandler<GlobalExceptionHandler>();  // 处理第一个异常
                                                                     //// 管道化
                                                                     //services.AddExceptionHandler<GlobalExceptionHandler2>(); // 紧接着处理第二个异常
                                                                     //services.AddExceptionHandler<GlobalExceptionHandler3>(); // 最后处理第三个异常

            return services;
        }

        public static void AddCorsService(this IServiceCollection services, Action<CorsOptions> config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            services.Configure(config);

            var option = new CorsOptions();
            config.Invoke(option);
            if (option.IsEnabled)
            {
                string policyName = option.PolicyName;
                services.AddCors(options =>
                {
                    options.AddPolicy(policyName, builder =>
                    {
                        builder.AllowAnyOrigin()
                            .WithOrigins(option.Origins)
                            .AllowAnyMethod()
                            //.AllowCredentials()
                            .AllowAnyHeader()
                            .SetPreflightMaxAge(TimeSpan.FromSeconds(1728000));
                        //通过配置设置是否允许全部来源跨域  CORE 2.1之后不允许AllowAnyOrigin和AllowCredentials同时使用必须指定 origin 来源                        
                        builder.SetIsOriginAllowed(origin => option.IsAllCors);
                    });
                });
            }
        }

        public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            var hcBuilder = services.AddHealthChecks();
            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());
            hcBuilder.AddCheck<SqlHealthCheck>(SqlHealthCheck.Name);
            return services;
        }

        public static IServiceCollection AddMySql(this IServiceCollection services, IConfiguration configuration)
        {
            //services.AddEntityFrameworkMySql(configuration.GetConnectionString("ActivationConnection")!);
            //services.AddSqlSugarService(configuration);

            return services;
        }

        public static IServiceCollection AddCustomConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions();

            services.Configure<MsPlatformDbContextSettings>(option =>
            {
                var setting = configuration.Get<MsPlatformDbContextSettings>() ?? throw new ArgumentException(nameof(MsPlatformDbContextSettings));
                option.AutoTimeTracker = setting.AutoTimeTracker;
                option.EnabledSoftDeleted = setting.EnabledSoftDeleted;
            });
            // ...这里添加Option配置
            services.Configure<IdentityOptions>(configuration.GetSection(IdentityOptions.Name));
            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services, IConfiguration configuration)
        {
            var swaggerOption = configuration.GetSection("SwaggerOptions").Get<SwaggerOptions>();
            if (swaggerOption != null)
                services.AddPlatformSwagger(option =>
                {
                    option.EnabledSecurity = swaggerOption.EnabledSecurity;
                    option.IsEnabled = swaggerOption.IsEnabled;
                    option.SwaggerXmlFile = swaggerOption.SwaggerXmlFile;
                    option.IsAuth = swaggerOption.IsAuth;
                    option.Name = swaggerOption.Name;
                    option.RoutePrefix = swaggerOption.RoutePrefix;
                });
            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    var jwtBearerOption = configuration.GetSection("IdentityOptions:JwtBearerOption").Get<ActivationJwtBearerOption>() ?? throw new ArgumentException(nameof(ActivationJwtBearerOption));
                    options.SaveToken = true;
                    options.RequireHttpsMetadata = false;

                    // 设置token属性
                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidateIssuerSigningKey = true,
                        ValidateAudience = true,
                        ValidAudiences = jwtBearerOption.Audiences,
                        ValidateLifetime = true,
                        ValidateIssuer = true,
                        ValidIssuers = jwtBearerOption.Issuers,
                        ClockSkew = System.TimeSpan.Zero,
                        RequireExpirationTime = true,
                        NameClaimType = JwtClaimTypes.NickName,
                        RoleClaimType = JwtClaimTypes.Role,
                    };
                    if (jwtBearerOption.SecurityKeys?.Length > 0)
                    {
                        var securityKeys = jwtBearerOption.SecurityKeys
                            .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));
                        options.TokenValidationParameters.IssuerSigningKeys = securityKeys;
                    }
                });
            services.AddAuthorization(option =>
            {
                var bearerOption = configuration.GetSection("IdentityOptions:JwtBearerOption").Get<ActivationJwtBearerOption>() ?? throw new ArgumentException(nameof(ActivationJwtBearerOption));
                // TODO
                option.AddPolicy("Manage", policy => policy.Requirements.Add(new RbacRequirement(bearerOption.Issuers!, ClaimTypes.Role, "")));
            });

            services.AddSingleton<IAuthorizationHandler, RbacAuthorizationHandler>();

            return services;
        }
    }
}