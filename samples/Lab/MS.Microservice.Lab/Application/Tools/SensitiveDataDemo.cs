using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Auditing;
using MS.Microservice.Core.Security.SensitiveData;
using MS.Microservice.Persistence.EFCore.Auditing;

namespace MS.Microservice.Lab.Application.Tools;

internal sealed class SensitiveDemoContext(DbContextOptions<SensitiveDemoContext> options, AuditOperationContext operation)
    : AuditedDbContext(options, operation)
{
    internal DbSet<SensitiveDemoEntity> Records => Set<SensitiveDemoEntity>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddAuditRecords();
        model.Entity<SensitiveDemoEntity>().HasKey(value => value.Id);
        model.Entity<SensitiveDemoEntity>().Property(value => value.Id).ValueGeneratedNever();
        model.Entity<SensitiveDemoEntity>().AuditChanges("sensitive-demo", value => new { value.Phone, value.Email, value.InternalNote });
    }
}
internal sealed class SensitiveDemoEntity
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = "";
    public string Name { get; set; } = "";
    [Sensitive(SensitiveKind.Phone)] public string Phone { get; set; } = "";
    [Sensitive(SensitiveKind.Email)] public string Email { get; set; } = "";
    [Sensitive(SensitiveKind.Secret)] public string InternalNote { get; set; } = "";
}
internal static class SensitiveDataDemo
{
    internal static IServiceCollection AddSensitiveDataDemo(this IServiceCollection services, IWebHostEnvironment environment)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, ".tools", "sensitive-data"));
        var webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot")));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory.Equals(webRoot, comparison) || directory.StartsWith(webRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Sensitive data demo storage must be outside webroot.");
        Directory.CreateDirectory(directory);
        services.AddDbContext<SensitiveDemoContext>(options => options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "records.db"), Pooling = false }.ToString()));
        services.AddHostedService<SensitiveDemoInitializer>();
        return services;
    }
}
internal sealed class SensitiveDemoInitializer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SensitiveDemoContext>().Database.EnsureCreatedAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
