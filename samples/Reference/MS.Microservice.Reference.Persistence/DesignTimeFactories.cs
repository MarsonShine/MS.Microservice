using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MS.Microservice.Reference.Persistence;

public sealed class SelfManagedContextFactory : IDesignTimeDbContextFactory<SelfManagedReferenceDbContext>
{
    public SelfManagedReferenceDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SelfManagedReferenceDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__ReferenceDatabase")
                ?? "Host=localhost;Database=ms_reference_self;Username=migrator",
            options => options.MigrationsHistoryTable("__MigrationsHistory", ReferenceDbContext.Schema)).Options);
}

public sealed class WolverineContextFactory : IDesignTimeDbContextFactory<WolverineReferenceDbContext>
{
    public WolverineReferenceDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<WolverineReferenceDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__ReferenceDatabase")
                ?? "Host=localhost;Database=ms_reference_wolverine;Username=migrator",
            options => options.MigrationsHistoryTable("__MigrationsHistory", "wolverine")).Options);
}
