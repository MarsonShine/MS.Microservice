using System.Security.Claims;
using System.Text.Encodings.Web;
using Coravel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.AspNetCore.Auditing;
using MS.Microservice.Caching;
using MS.Microservice.Core.Security.SensitiveData;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Tasks.Coravel;
using MS.Microservice.Tasks.Dashboard;

namespace MS.Microservice.Lab.Tests.Tools;

[CollectionDefinition("Tool demos", DisableParallelization = true)]
public sealed class ToolDemoCollection;

internal sealed class ToolDemoTestHost : IDisposable
{
    private readonly WebApplication _app;
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ms-tool-api-tests-" + Guid.NewGuid().ToString("N"));
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;

    public ToolDemoTestHost(IDictionary<string, string?>? configuration = null, bool includeLab = true, bool includeTaskCenter = false, bool includeAuditing = false, bool includeSensitiveData = false)
    {
        Directory.CreateDirectory(Root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = Root, EnvironmentName = "Lab", ApplicationName = typeof(ToolDemoFiles).Assembly.GetName().Name
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configuration ?? new Dictionary<string, string?>());
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ToolDemoFiles).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.UseSensitiveValues())
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new LabOnlyControllerFeatureProvider(includeLab)))
            .AddAutomaticAuditOperations(options => options.ResolveIdentity = context =>
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } owner ? new(TaskCenterDemo.Scope(owner), owner) : null);
        builder.Services.AddScoped<FileUploadValidator>();
        builder.Services.AddToolDemoApi();
        builder.Services.AddGovernedCaching();
        builder.Services.AddSingleton<CacheDemo>();
        if (includeAuditing) builder.Services.AddAuditDemo(builder.Environment, builder.Configuration);
        if (includeSensitiveData) builder.Services.AddSensitiveDataDemo(builder.Environment);
        if (includeTaskCenter) builder.Services.AddTaskCenterDemo(builder.Environment, builder.Configuration);
        builder.Services.AddAuthentication("ToolDemoTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("ToolDemoTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddPlatformSwagger(options =>
        {
            options.EnabledSecurity = true;
            options.SwaggerXmlFile = Path.ChangeExtension(typeof(ToolDemoFiles).Assembly.Location, ".xml");
        });
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UsePlatformSwagger();
        _app.MapControllers();
        if (includeTaskCenter)
        {
            _app.MapTaskDashboard();
            _app.Services.UseSchedulerWithTaskDashboard(scheduler =>
                scheduler.ScheduleTaskCenter().WithDashboardName("持久任务派发"));
        }
        _app.StartAsync().GetAwaiter().GetResult();
        Client = _app.GetTestClient();
        Client.Timeout = TimeSpan.FromMinutes(10);
        Client.DefaultRequestHeaders.Add("X-Test-User", "alice");
    }

    public void Dispose()
    {
        Client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Directory.Delete(Root, recursive: true);
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
