using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Lab.Application.Orders;
using MS.Microservice.Lab.Application.Identity.Token;
using MS.Microservice.Lab.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Lab.Infrastructure.Authorizations.Requirements;
using MS.Microservice.Lab.Infrastructure.Cors;
using MS.Microservice.Lab.Infrastructure.Filters;
using MS.Microservice.Lab.Infrastructure.Uploads;
using MS.Microservice.Swagger;

namespace MS.Microservice.Lab.Infrastructure.Extensions
{
    public static partial class IServiceCollectionExtensions
    {
        private const int MinimumJwtSecurityKeyLength = 32;

        extension(IServiceCollection services)
        {
            public IServiceCollection AddCoreServices([NotNull] IConfiguration configuration)
            {
                services
                    .AddCustomMvc(configuration)
                    .AddHealthChecks(configuration)
                    .AddApplicationInfrastructure(configuration)
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
                services.AddApplicationCaching(configuration);

                services.AddSingleton(new MS.Microservice.Core.Serialization.JsonTypeRegistry());
                services.AddHttpClient<LogHttpClient>();
                services.AddScoped<FileUploadValidator>();
                services.AddScoped<IUploadStorage, LocalUploadStorage>();

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
                hcBuilder.AddCheck(
                    "self",
                    () => HealthCheckResult.Healthy(),
                    tags: ["live"]);
                hcBuilder.AddCheck<SqlHealthCheck>(
                    SqlHealthCheck.Name,
                    tags: [SqlHealthCheck.ReadinessTag]);
                return services;
            }

            public IServiceCollection AddApplicationCaching(IConfiguration configuration)
            {
                ArgumentNullException.ThrowIfNull(configuration);

                var section = configuration.GetSection(CacheOptions.SectionName);
                services.AddOptions<CacheOptions>()
                    .Bind(section)
                    .Validate(
                        options => options.SlidingExpirationSecond > 0,
                        "CacheOptions:SlidingExpirationSecond must be greater than zero.")
                    .Validate(
                        options => options.AbsoluteExpirationSecond is null or > 0,
                        "CacheOptions:AbsoluteExpirationSecond must be greater than zero when configured.")
                    .ValidateOnStart();

                var cacheOptions = section.Get<CacheOptions>() ?? new CacheOptions();
                var expirationScanFrequency = TimeSpan.FromSeconds(cacheOptions.SlidingExpirationSecond);

                // HybridCache is the preferred high-level API. Existing application services still
                // consume IDistributedCache, so a replaceable compatibility backend is registered too.
                services.AddMemoryCache(options =>
                {
                    options.ExpirationScanFrequency = expirationScanFrequency;
                });
                services.AddDistributedMemoryCache(options =>
                {
                    options.ExpirationScanFrequency = expirationScanFrequency;
                });

#if NET9_0_OR_GREATER
                services.AddHybridCache();
#endif

                return services;
            }

            public IServiceCollection AddApplicationInfrastructure(IConfiguration configuration)
            {
                services.AddMicroserviceEfCorePersistence(configuration);
                services.AddMicroserviceSqlSugarPersistence(configuration);
                var eventStore = configuration.GetConnectionString("EventStoreConnection");
                if (string.IsNullOrWhiteSpace(eventStore))
                    throw new InvalidOperationException("ConnectionStrings:EventStoreConnection is required.");
                services.AddPostgresEventSourcing(eventStore);
                services.AddMsOpenTelemetry(configuration);
                services.AddScoped<IOrderWorkflowAppService, OrderWorkflowAppService>();
                services.AddScoped<IOrderQueryAppService, OrderQueryAppService>();

                return services;
            }

            public IServiceCollection AddCustomConfiguration(IConfiguration configuration)
            {
                services.AddOptions();
                services.AddSingleton(TimeProvider.System);
                services.AddOptions<LabTokenIssuerOptions>()
                    .Bind(configuration.GetSection(LabTokenIssuerOptions.SectionName))
                    .PostConfigure(options => options.Validate())
                    .ValidateOnStart();

                services.AddOptions<IdentityOptions>()
                    .Bind(configuration.GetSection(IdentityOptions.Name))
                    .PostConfigure(options => options.JwtBearerOption = LabTokenIssuerOptions.ValidationOptions(configuration))
                    .Validate(options => options.JwtBearerOption is not null, "IdentityOptions:JwtBearerOption is required.")
                    .Validate(options => options.JwtBearerOption?.Audiences?.Length > 0, "IdentityOptions:JwtBearerOption:Audiences is required.")
                    .Validate(options => options.JwtBearerOption?.Issuers?.Length > 0, "IdentityOptions:JwtBearerOption:Issuers is required.")
                    .Validate(options => options.JwtBearerOption?.SecurityKeys?.Length > 0, "IdentityOptions:JwtBearerOption:SecurityKeys is required.")
                    .Validate(
                        options => options.JwtBearerOption?.SecurityKeys is not { Length: > 0 } securityKeys || securityKeys.All(IsValidJwtSecurityKey),
                        $"IdentityOptions:JwtBearerOption:SecurityKeys entries must contain at least {MinimumJwtSecurityKeyLength} ASCII characters.")
                    .ValidateOnStart();

                services.AddOptions<SampleUploadOptions>()
                    .Bind(configuration.GetSection(SampleUploadOptions.SectionName))
                    .Validate(
                        options => options.MaxImageBytes is > 0 and <= SampleUploadOptions.MaximumConfiguredFileBytes,
                        $"SampleUploadOptions:MaxImageBytes must be between 1 and {SampleUploadOptions.MaximumConfiguredFileBytes} bytes.")
                    .Validate(
                        options => options.MaxExcelBytes is > 0 and <= SampleUploadOptions.MaximumConfiguredFileBytes,
                        $"SampleUploadOptions:MaxExcelBytes must be between 1 and {SampleUploadOptions.MaximumConfiguredFileBytes} bytes.")
                    .Validate(
                        options => SampleUploadOptions.IsSafeStorageDirectory(options.StorageDirectory),
                        "SampleUploadOptions:StorageDirectory must be a safe relative path below the content root.")
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
                var securityKeys = GetRequiredSecurityKeys(jwtBearerOption.SecurityKeys);

                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.SaveToken = false;
                        options.MapInboundClaims = false;
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
                    option.AddPolicy("LabMessagingOperations", policy => policy.RequireAuthenticatedUser()
                        .AddRequirements(new RbacRequirement(issuers, JwtClaimTypes.Role,
                            MS.Microservice.Domain.Consts.LabPermissions.MessagingOperations)));
                    option.AddPolicy("Manage", policy => policy.Requirements.Add(new RbacRequirement(issuers, JwtClaimTypes.Role, "")));
                });

                services.AddScoped<IAuthorizationHandler, RbacAuthorizationHandler>();

                return services;
            }

            private static ActivationJwtBearerOption GetRequiredJwtBearerOption(IConfiguration configuration)
                => LabTokenIssuerOptions.ValidationOptions(configuration);

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

            private static string[] GetRequiredSecurityKeys(string[]? values)
            {
                const string configurationPath = "IdentityOptions:JwtBearerOption:SecurityKeys";
                var securityKeys = GetRequiredValues(values, configurationPath);

                if (securityKeys.Any(key => !IsValidJwtSecurityKey(key)))
                {
                    throw new OptionsValidationException(
                        configurationPath,
                        typeof(string[]),
                        [$"{configurationPath} entries must contain at least {MinimumJwtSecurityKeyLength} ASCII characters."]);
                }

                return securityKeys;
            }

            private static bool IsValidJwtSecurityKey(string? securityKey)
                => !string.IsNullOrWhiteSpace(securityKey)
                    && securityKey.Length >= MinimumJwtSecurityKeyLength
                    && securityKey.All(char.IsAscii);
        }
    }
}
