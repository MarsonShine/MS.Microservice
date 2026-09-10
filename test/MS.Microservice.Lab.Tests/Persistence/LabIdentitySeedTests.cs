using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Domain.Events;
using MS.Microservice.Lab.Persistence;

namespace MS.Microservice.Lab.Tests;

public sealed class LabIdentitySeedTests
{
    private const string OperatorPassword = "operator-secret-for-tests";
    private const string ReaderPassword = "reader-secret-for-tests";

    [Fact]
    public async Task ExplicitBootstrapCreatesHashedUsersAndPreservesExistingCredentials()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        await LabIdentitySeed.SeedAsync(context, OperatorPassword, ReaderPassword);
        context.ChangeTracker.Clear();
        var user = await context.Users.Include(x => x.Roles).ThenInclude(x => x.Actions).SingleAsync(x => x.Account == "lab-operator");
        foreach (var entry in context.ChangeTracker.Entries())
        foreach (var property in entry.Properties)
            if (property.CurrentValue is string text && property.Metadata.GetMaxLength() is { } limit)
                Assert.InRange(text.Length, 0, limit);
        Assert.Equal(2, await context.Users.Select(x => x.FzAccount).Distinct().CountAsync());
        var original = user.Password;
        Assert.True(user.HasModernPasswordHash());
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(user, user.Password!, OperatorPassword));
        Assert.Equal(LabPermissions.MessagingOperations, Assert.Single(Assert.Single(user.Roles).Actions).Path);
        await LabIdentitySeed.SeedAsync(context, "different-operator-password", "different-reader-password");
        Assert.Equal(2, await context.Users.CountAsync());
        Assert.Equal(1, await context.Roles.CountAsync());
        Assert.Equal(original, user.Password);
        Assert.Empty((await context.Users.Include(x => x.Roles).SingleAsync(x => x.Account == "lab-reader")).Roles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("               ")]
    public async Task InvalidBootstrapSecretProducesNoUsers(string password)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => LabIdentitySeed.SeedAsync(context, password, ReaderPassword));
        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.Roles.ToListAsync());
    }

    private static ActivationDbContext Context(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<ActivationDbContext>().UseSqlite(connection).Options,
        Options.Create(new MsPlatformDbContextSettings()), new NoOpDomainEventDispatcher());
}
