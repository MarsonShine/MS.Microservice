using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.Core.Ceching;
using MS.Microservice.Core.Identity;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Infrastructure.HealthChecks;
using MS.Microservice.Web.Application.Orders;
using MS.Microservice.Web.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Web.Infrastructure.Filters;
using MS.Microservice.Swagger;

namespace MS.Microservice.Web.Infrastructure.Extensions
{
    public static partial class IServiceCollectionExtensions
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddCoreServices([NotNull] IConfiguration configuration)
            {
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

            public IServiceCollection AddCustomMvc(IConfiguration configuration)
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

                services.AddCorsService(configuration);

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

            public void AddCorsService(IConfiguration configuration)
            {
                ArgumentNullException.ThrowIfNull(configuration);

                var section = configuration.GetSection(CorsOptions.SectionName);
                services.AddOptions<CorsOptions>()
                    .Bind(section)
                    .Validate(option => !option.IsEnabled || !string.IsNullOrWhiteSpace(option.PolicyName), "CorsOptions:PolicyName is required when CORS is enabled.")
                    .Validate(option => !option.IsEnabled || option.IsAllCors || option.Origins.Length > 0, "CorsOptions:Origins must be configured when CORS is enabled and IsAllCors is false.")
                    .ValidateOnStart();

                var option = section.Get<CorsOptions>() ?? new CorsOptions();
                if (option.IsEnabled)
                {
                    string policyName = option.PolicyName;
                    services.AddCors(options =>
                    {
                        options.AddPolicy(policyName, builder =>
                        {
                            if (option.IsAllCors)
                            {
                                builder.AllowAnyOrigin();
                            }
                            else
                            {
                                builder.WithOrigins(option.Origins);
                            }

                            builder
                                .AllowAnyMethod()
                                //.AllowCredentials()
                                .AllowAnyHeader()
                                .SetPreflightMaxAge(TimeSpan.FromSeconds(1728000));
                        });
                    });
                }
            }

            public IServiceCollection AddHealthChecks(IConfiguration configuration)
            {
                var hcBuilder = services.AddHealthChecks();
                hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());
                hcBuilder.AddCheck<SqlHealthCheck>(SqlHealthCheck.Name);
                return services;
            }

            public IServiceCollection AddMySql(IConfiguration configuration)
            {
                //services.AddEntityFrameworkMySql(configuration.GetConnectionString("ActivationConnection")!);
                //services.AddMicroserviceSqlSugarPersistence(configuration);
                services.AddInfrastructure(configuration);
                services.AddScoped<IOrderWorkflowAppService, OrderWorkflowAppService>();
                services.AddScoped<IOrderQueryAppService, OrderQueryAppService>();

                return services;
            }

            public IServiceCollection AddCustomConfiguration(IConfiguration configuration)
            {
                services.AddOptions();

                services.AddOptions<IdentityOptions>()
                    .Bind(configuration.GetSection(IdentityOptions.Name))
                    .Validate(options => options.JwtBearerOption is not null, "IdentityOptions:JwtBearerOption is required.")
                    .Validate(options => options.JwtBearerOption?.Audiences?.Length > 0, "IdentityOptions:JwtBearerOption:Audiences is required.")
                    .Validate(options => options.JwtBearerOption?.Issuers?.Length > 0, "IdentityOptions:JwtBearerOption:Issuers is required.")
                    .Validate(options => options.JwtBearerOption?.SecurityKeys?.Length > 0, "IdentityOptions:JwtBearerOption:SecurityKeys is required.")
                    .ValidateOnStart();

                return services;
            }

            public IServiceCollection AddCustomSwagger(IConfiguration configuration)
            {
                services.AddPlatformSwagger(options =>
                {
                    configuration.GetSection(SwaggerOptions.SectionName).Bind(options);
                });
                return services;
            }

            public IServiceCollection AddCustomAuthentication(IConfiguration configuration)
            {
                var jwtBearerOption = GetRequiredJwtBearerOption(configuration);
                var audiences = GetRequiredValues(jwtBearerOption.Audiences, "IdentityOptions:JwtBearerOption:Audiences");
                var issuers = GetRequiredValues(jwtBearerOption.Issuers, "IdentityOptions:JwtBearerOption:Issuers");
                var securityKeys = GetRequiredValues(jwtBearerOption.SecurityKeys, "IdentityOptions:JwtBearerOption:SecurityKeys");

                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.SaveToken = true;
                        options.RequireHttpsMetadata = false;

                        // 设置token属性
                        options.TokenValidationParameters = new TokenValidationParameters()
                        {
                            ValidateIssuerSigningKey = true,
                            ValidateAudience = true,
                            ValidAudiences = audiences,
                            ValidateLifetime = true,
                            ValidateIssuer = true,
                            ValidIssuers = issuers,
                            ClockSkew = System.TimeSpan.Zero,
                            RequireExpirationTime = true,
                            NameClaimType = JwtClaimTypes.NickName,
                            RoleClaimType = JwtClaimTypes.Role,
                        };
                        options.TokenValidationParameters.IssuerSigningKeys = securityKeys
                            .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));
                    });
                services.AddAuthorization(option =>
                {
                    // TODO
                    option.AddPolicy("Manage", policy => policy.Requirements.Add(new RbacRequirement(issuers, ClaimTypes.Role, "")));
                });

                services.AddSingleton<IAuthorizationHandler, RbacAuthorizationHandler>();

                return services;
            }

            private static ActivationJwtBearerOption GetRequiredJwtBearerOption(IConfiguration configuration)
            {
                var option = configuration
                    .GetSection($"{IdentityOptions.Name}:JwtBearerOption")
                    .Get<ActivationJwtBearerOption>();

                if (option is null)
                {
                    throw new OptionsValidationException(
                        nameof(ActivationJwtBearerOption),
                        typeof(ActivationJwtBearerOption),
                        ["IdentityOptions:JwtBearerOption is required."]);
                }

                return option;
            }

            private static string[] GetRequiredValues(string[]? values, string configurationPath)
            {
                if (values is null || values.Length == 0)
                {
                    throw new OptionsValidationException(
                        configurationPath,
                        typeof(string[]),
                        [$"{configurationPath} is required."]);
                }

                return values;
            }
        }
    }
}
