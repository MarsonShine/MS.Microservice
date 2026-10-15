using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Lab.Application.Tools;

internal sealed class AuditDemoContext(DbContextOptions<AuditDemoContext> options, AuditOperationContext operation)
    : AuditedDbContext(options, operation)
{
    internal DbSet<AuditDemoOrder> Orders => Set<AuditDemoOrder>();
    internal DbSet<AuditDemoStock> Stock => Set<AuditDemoStock>();
    internal DbSet<AuditDemoApproval> Approvals => Set<AuditDemoApproval>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddAuditRecords();
        model.Entity<AuditDemoOrder>().HasKey(value => value.Id);
        model.Entity<AuditDemoOrder>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<AuditDemoOrder>().Property(value => value.Version).IsConcurrencyToken();
        model.Entity<AuditDemoOrder>().AuditChanges("order", value => new { value.Status, value.Quantity });
        model.Entity<AuditDemoStock>().HasKey(value => value.Id);
        model.Entity<AuditDemoStock>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<AuditDemoStock>().HasOne<AuditDemoOrder>().WithMany().HasForeignKey(value => value.OrderId);
        model.Entity<AuditDemoStock>().AuditChanges("stock", value => new { value.Sku, value.Available });
        model.Entity<AuditDemoApproval>().HasKey(value => value.Id);
        model.Entity<AuditDemoApproval>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<AuditDemoApproval>().HasOne<AuditDemoOrder>().WithMany().HasForeignKey(value => value.OrderId);
        model.Entity<AuditDemoApproval>().AuditChanges("approval", value => value.Note);
    }
}

internal sealed class AuditDemoOrder
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Quantity { get; set; }
    public int Version { get; set; } = 1;
}
internal sealed class AuditDemoStock
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Available { get; set; } = 100;
}
internal sealed class AuditDemoApproval
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Note { get; set; } = "";
}

internal static class AuditDemo
{
    internal static IServiceCollection AddAuditDemo(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            ToolDemoDefaults.Value(configuration, "AuditStorageDirectory") ?? ".tools/audit-demo"));
        var webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot")));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory.Equals(webRoot, comparison) || directory.StartsWith(webRoot + Path.DirectorySeparatorChar, comparison))
            throw new ToolDependencyException("ToolDemos:AuditStorageDirectory（必须位于 webroot 之外）");
        Directory.CreateDirectory(directory);
        services.AddDbContext<AuditDemoContext>(options => options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "auditing.db"), Pooling = false }.ToString()));
        services.AddHostedService<AuditDemoInitializer>();
        return services;
    }
}
internal sealed class AuditDemoInitializer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditDemoContext>().Database.EnsureCreatedAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
