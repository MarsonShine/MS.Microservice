using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Tasks.EFCore;

namespace MS.Microservice.Tasks.Tests;

internal sealed class JobDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms-job-store-tests-" + Guid.NewGuid().ToString("N"));
    public ServiceProvider Services { get; private set; } = null!;
    public JobClock Clock { get; } = new();

    public static async Task<JobDatabase> CreateAsync()
    {
        var fixture = new JobDatabase();
        Directory.CreateDirectory(fixture._directory);
        fixture.Services = fixture.CreateProvider();
        try
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<JobTestContext>().Database.EnsureCreatedAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task RestartAsync()
    {
        await Services.DisposeAsync();
        Services = CreateProvider();
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDbContext<JobTestContext>(options => options.UseSqlite(
            "Data Source=" + Path.Combine(_directory, "jobs.db") + ";Pooling=false"));
        services.AddEfCoreJobStore<JobTestContext>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class JobClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 14, 4, 5, 6, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now.AddTicks(1234567);
    public void Advance(TimeSpan elapsed) => _now += elapsed;
}

internal sealed class JobTestContext(DbContextOptions<JobTestContext> options) : DbContext(options)
{
    public DbSet<BusinessRecord> Business => Set<BusinessRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddTaskCenter();
        model.Entity<BusinessRecord>().HasKey(entry => entry.Id);
    }
}

internal sealed class BusinessRecord
{
    public Guid Id { get; set; }
    public string Value { get; set; } = "";
}
