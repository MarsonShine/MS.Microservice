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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.Core.Ceching;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Swagger.Swagger;
using MS.Microservice.Web.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Web.Infrastructure.Filters;

namespace MS.Microservice.Web.Infrastructure.Extensions {
    public static class IServiceCollectionExtensions {
        public static IServiceCollection AddFzPlatformServices(this IServiceCollection services, [NotNull] IConfiguration configuration) {
            services
                .AddCustomMvc(configuration)
                .AddHealthChecks(configuration)
                // .AddMySql(configuration)
                .AddCustomSwagger(configuration)
                .AddCustomConfiguration(configuration)
                .AddCustomAuthentication(configuration);

            return services;
        }

        public static IServiceCollection AddCustomMvc(this IServiceCollection services, IConfiguration configuration) {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddControllers(options => {
                    options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                    options.Filters.Add(typeof(ApiEncryptActionExecutingFilter));
                }).AddJsonOptions(options => {
                    options.JsonSerializerOptions.WriteIndented = true;
                    // 这里添加自定义json转换器
                    // options.JsonSerializerOptions.Converters.Add(new MyCustomJsonConverter());
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

            services.AddCorsService(option => {
                var cors = configuration.GetSection("CorsOptions").Get<CorsOptions>();
                option.IsEnabled = cors.IsEnabled;
                option.PolicyName = cors.PolicyName;
                option.Origins = cors.Origins;
                option.IsAllCors = cors.IsAllCors;
            });

            services.AddMemoryCache(options => {
                    var cacheOptions = configuration.GetSection("CacheOptions").Get<ActivationCacheOptions>();
                    options.ExpirationScanFrequency = System.TimeSpan.FromSeconds(cacheOptions.SlidingExpirationSecond);
                })
                .AddDistributedMemoryCache(options => {
                    var cacheOptions = configuration.GetSection("CacheOptions").Get<ActivationCacheOptions>();
                    options.ExpirationScanFrequency = System.TimeSpan.FromSeconds(cacheOptions.SlidingExpirationSecond);
                });

            return services;
        }

        public static void AddCorsService(this IServiceCollection services, Action<CorsOptions> config) {
            if (config == null) throw new ArgumentNullException(nameof(config));
            services.Configure(config);

            var option = new CorsOptions();
            config.Invoke(option);
            if (option.IsEnabled) {
                string policyName = option.PolicyName;
                services.AddCors(options => {
                    options.AddPolicy(policyName, builder => {
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

        public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration) {
            var hcBuilder = services.AddHealthChecks();
            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            return services;
        }

        public static IServiceCollection AddMySql(this IServiceCollection services, IConfiguration configuration) {
            services.AddEntityFrameworkMySql(configuration.GetConnectionString("ActivationConnection"));
            return services;
        }

        public static IServiceCollection AddCustomConfiguration(this IServiceCollection services, IConfiguration configuration) {
            services.AddOptions();

            services.Configure<MsPlatformDbContextSettings>(option => {
                var setting = configuration.Get<MsPlatformDbContextSettings>();
                option.AutoTimeTracker = setting.AutoTimeTracker;
                option.EnabledSoftDeleted = setting.EnabledSoftDeleted;
            });
            // ...这里添加Option配置
            services.Configure<IdentityOptions>(configuration.GetSection(IdentityOptions.Name));
            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services, IConfiguration configuration) {
            var swaggerOption = configuration.GetSection("SwaggerOptions").Get<SwaggerOptions>();
            services.AddPlatformSwagger(option => {
                option.EnabledSecurity = swaggerOption.EnabledSecurity;
                option.IsEnabled = swaggerOption.IsEnabled;
                option.SwaggerXmlFile = swaggerOption.SwaggerXmlFile;
            });
            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration) {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => {
                    var jwtBearerOption = configuration.GetSection("IdentityOptions:JwtBearerOption").Get<ActivationJwtBearerOption>();
                    options.SaveToken = true;
                    options.RequireHttpsMetadata = false;

                    // 设置token属性
                    options.TokenValidationParameters = new TokenValidationParameters() {
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
                    if (jwtBearerOption.SecurityKeys.Length > 0) {
                        var securityKeys = jwtBearerOption.SecurityKeys
                            .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));
                        options.TokenValidationParameters.IssuerSigningKeys = securityKeys;
                    }
                });
            services.AddAuthorization(option => {
                var bearerOption = configuration.GetSection("IdentityOptions:JwtBearerOption").Get<ActivationJwtBearerOption>();
                // TODO
                option.AddPolicy("Manage", policy => policy.Requirements.Add(new RbacRequirement(bearerOption.Issuers, ClaimTypes.Role, "")));
            });

            services.AddSingleton<IAuthorizationHandler, RbacAuthorizationHandler>();

            return services;
        }
    }
}