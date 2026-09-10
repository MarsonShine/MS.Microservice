using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class AuditTimestampTests
{
    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Unchanged)]
    [InlineData(EntityState.Deleted)]
    public void IndependentAuditInterfacesRespectEntityState(EntityState state)
    {
        using var context = new AuditContext(new DbContextOptionsBuilder<AuditContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var original = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero);
        var created = new Created { Id = 1, CreatedAt = original };
        var updated = new Updated { Id = 2, UpdatedAt = original };
        var both = new Both { Id = 3, CreatedAt = original, UpdatedAt = original };
        context.Entry(created).State = state;
        context.Entry(updated).State = state;
        context.Entry(both).State = state;

        context.UpdateAuditTimestamps(new FixedClock(now));

        Assert.Equal(state == EntityState.Added ? now.UtcDateTime : original, created.CreatedAt);
        Assert.Equal(state == EntityState.Added ? now.UtcDateTime : original, both.CreatedAt);
        var expectedUpdated = state is EntityState.Added or EntityState.Modified ? now.UtcDateTime : original;
        Assert.Equal(expectedUpdated, updated.UpdatedAt);
        Assert.Equal(expectedUpdated, both.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, both.CreatedAt.Kind);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class AuditContext(DbContextOptions<AuditContext> options) : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Created>();
            modelBuilder.Entity<Updated>();
            modelBuilder.Entity<Both>();
        }
    }
    private sealed class Created : ICreatedAt { public int Id { get; set; } public DateTime CreatedAt { get; set; } }
    private sealed class Updated : IUpdatedAt { public int Id { get; set; } public DateTime? UpdatedAt { get; set; } }
    private sealed class Both : ICreatedAt, IUpdatedAt
    { public int Id { get; set; } public DateTime CreatedAt { get; set; } public DateTime? UpdatedAt { get; set; } }
}
