using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using MS.Microservice.Web.Infrastructure.Extensions;
using MS.Microservice.Web.Infrastructure.Mvc.ModelBinder.Extension;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Autofac;
using Microsoft.Extensions.Configuration;
using MS.Microservice.Web.AutofacModules.Extensions;
using MS.Microservice.Web.Infrastructure.Cors;
using MS.Microservice.Infrastructure.Common.Extensions;
using Microsoft.AspNetCore.ResponseCompression;
using MS.Microservice.Core.FeatureManager;
using MS.Microservice.Swagger.Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigurePlatformLogging(cfg =>
{
    cfg.NLogConfiguration();
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
		options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
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
		// 可以选择配置压缩的类型
		options.EnableForHttps = true;  // 启用 HTTPS 时的响应压缩
		options.Providers.Add<BrotliCompressionProvider>();  // 使用 Brotli 压缩
		options.Providers.Add<GzipCompressionProvider>();   // 使用 Gzip 压缩
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
// 顺序不能错
app.UseDefaultFiles();
app.UseStaticFiles();
//app.UsePathBase(new Microsoft.AspNetCore.Http.PathString());

app.MapControllers();
app.MapHealthChecks("/hc");
app.UseEnableBuffering();

app.UseResponseCompression();
app.UsePlatformLogger();

app.Run();