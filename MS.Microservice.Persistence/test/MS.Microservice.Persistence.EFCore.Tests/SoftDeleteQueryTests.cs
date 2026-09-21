using Microsoft.EntityFrameworkCore;
using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class SoftDeleteQueryTests
{
    [Fact]
    public async Task TypedFilterExcludesEveryNonNullDeletionDate_AndCanBeIgnoredOrRestored()
    {
        await using var context = new FilterContext(new DbContextOptionsBuilder<FilterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var deleted = new Row { Id = 2, DeletedAt = new DateTime(2000, 1, 1) };
        context.AddRange(new Row { Id = 1 }, deleted,
            new Row { Id = 3, DeletedAt = new DateTime(2100, 1, 1) });
        context.Add(new PlainRow { Id = 1, DeletedAt = new DateTime(2000, 1, 1) });
        await context.SaveChangesAsync();

        Assert.Equal(new[] { 1 }, await context.Set<Row>().Select(row => row.Id).ToArrayAsync());
        Assert.Equal(3, await context.Set<Row>().IgnoreQueryFilters().CountAsync());
        Assert.Single(await context.Set<PlainRow>().ToListAsync());

        deleted.DeletedAt = null;
        await context.SaveChangesAsync();
        Assert.Equal(new[] { 1, 2 }, await context.Set<Row>().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
    }

    [Fact]
    public void TypedRegistrationRetainsDeletedAtIndex_WithoutDuplicatingIt()
    {
        using var context = new FilterContext(new DbContextOptionsBuilder<FilterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = context.Model.FindEntityType(typeof(Row))!;
        Assert.NotNull(entity.GetDeclaredQueryFilters().Single().Expression);
        Assert.Equal(nameof(Row.DeletedAt), Assert.Single(Assert.Single(entity.GetIndexes()).Properties).Name);
        Assert.Empty(context.Model.FindEntityType(typeof(PlainRow))!.GetDeclaredQueryFilters());
    }

    private sealed class FilterContext(DbContextOptions<FilterContext> options) : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Row>().AddSoftDeletedQueryFilter().AddSoftDeletedQueryFilter();
            builder.Entity<PlainRow>();
        }
    }

    private sealed class Row : ISoftDeleted
    {
        public int Id { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private sealed class PlainRow
    {
        public int Id { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
