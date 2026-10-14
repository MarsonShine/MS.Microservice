using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Tasks.EFCore;

namespace MS.Microservice.Tasks.Dashboard.Tests;

internal sealed class DashboardTestHost : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms-dashboard-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal IServiceProvider Services => _app.Services;

    internal static async Task<DashboardTestHost> CreateAsync(bool readOnly = false)
    {
        var host = new DashboardTestHost();
        Directory.CreateDirectory(host._directory);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(host._directory, "keys")));
        builder.Services.AddDbContext<DashboardContext>(options => options.UseSqlite("Data Source=" + Path.Combine(host._directory, "tasks.db") + ";Pooling=false"));
        builder.Services.AddEfCoreJobStore<DashboardContext>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("Operators", policy => policy.RequireRole("Operator")));
        builder.Services.AddTaskDashboard(options =>
        {
            options.ResolveScope = context => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            options.ReadOnly = readOnly;
            options.ControlAuthorizationPolicy = "Operators";
            options.SchedulesAuthorizationPolicy = "Operators";
        });
        host._app = builder.Build();
        host._app.UseAuthentication();
        host._app.UseAuthorization();
        host._app.MapTaskDashboard();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DashboardContext>().Database.EnsureCreatedAsync();
        await host._app.StartAsync();
        host.Client = host._app.GetTestClient();
        return host;
    }

    internal void Login(string name = "alice", bool isOperator = true)
    {
        Client.DefaultRequestHeaders.Remove("X-Test-User");
        Client.DefaultRequestHeaders.Remove("X-Test-Operator");
        Client.DefaultRequestHeaders.Add("X-Test-User", name);
        if (isOperator) Client.DefaultRequestHeaders.Add("X-Test-Operator", "true");
    }

    internal async Task ConnectAsync()
    {
        using var response = await Client.GetAsync("/task-center/api/session");
        response.EnsureSuccessStatusCode();
        using var session = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0])));
        var header = session.RootElement.GetProperty("csrfHeaderName").GetString()!;
        Client.DefaultRequestHeaders.Remove(header);
        Client.DefaultRequestHeaders.Add(header, session.RootElement.GetProperty("csrfRequestToken").GetString());
    }

    internal async Task<JobSnapshot> SubmitAsync(string scope = "alice", string payload = "{\"secret\":\"not-for-browser\"}")
    {
        await using var services = Services.CreateAsyncScope();
        return await services.ServiceProvider.GetRequiredService<IJobStore>().SubmitAsync(new(Guid.NewGuid(), scope, "demo", 1, payload));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var name = Request.Headers["X-Test-User"].ToString();
            if (name.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>();
            if (name != "no-scope") claims.Add(new(ClaimTypes.NameIdentifier, name));
            if (Request.Headers["X-Test-Operator"] == "true") claims.Add(new(ClaimTypes.Role, "Operator"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}

internal sealed class DashboardContext(DbContextOptions<DashboardContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTaskCenter();
}
