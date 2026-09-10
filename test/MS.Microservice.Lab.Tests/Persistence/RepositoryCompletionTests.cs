using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class RepositoryCompletionTests
{
    [Fact]
    public async Task UserRepository_FindAsync_ReturnsMatchingUser()
    {
        await using var context = CreateContext();
        var expected = CreateUser("find-user");
        context.Users.AddRange(expected, CreateUser("other-user"));
        await context.SaveChangesAsync();
        var repository = new UserRepository(context);

        var actual = await repository.FindAsync(user => user.Account == "find-user");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UserRepository_FindOptionAsync_ReturnsSomeForMatchingUser()
    {
        await using var context = CreateContext();
        var expected = CreateUser("option-user");
        context.Users.Add(expected);
        await context.SaveChangesAsync();
        var repository = new UserRepository(context);

        var option = await repository.FindOptionAsync(user => user.Account == "option-user");

        option.IsSome.Should().BeTrue();
        option.Match(() => false, user => ReferenceEquals(user, expected)).Should().BeTrue();
    }

    [Fact]
    public async Task UserRepository_GetAsync_ReturnsUserById()
    {
        await using var context = CreateContext();
        var expected = CreateUser("get-user");
        context.Users.Add(expected);
        await context.SaveChangesAsync();
        var repository = new UserRepository(context);

        var actual = await repository.GetAsync(expected.Id);

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UserRepository_GetAllRoleAsync_ReturnsRolesWithoutTracking()
    {
        await using var context = CreateContext();
        var expected = new Role(10, "admin", "administrator");
        context.Roles.Add(expected);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new UserRepository(context);

        var roles = await repository.GetAllRoleAsync();

        roles.Should().ContainSingle(role => role.Id == expected.Id);
        context.ChangeTracker.Entries<Role>().Should().BeEmpty();
    }

    [Fact]
    public async Task UserRepository_InsertAsync_ReplacesRoleReferencesWithStoredRoles()
    {
        await using var context = CreateContext();
        var storedRole = new Role(20, "stored", "stored role");
        context.Roles.Add(storedRole);
        await context.SaveChangesAsync();
        var user = CreateUser("insert-user");
        user.AddRole(new Role(20, "detached", "detached role"));
        var repository = new UserRepository(context);

        var inserted = await repository.InsertAsync(user);

        inserted.Should().BeSameAs(user);
        inserted.Roles.Should().ContainSingle().Which.Should().BeSameAs(storedRole);
        context.Entry(user).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task UserRepository_InsertEitherAsync_ReturnsRightWithInsertedUser()
    {
        await using var context = CreateContext();
        var user = CreateUser("either-user");
        var repository = new UserRepository(context);

        var result = await repository.InsertEitherAsync(user);

        result.IsRight.Should().BeTrue();
        result.Right.Should().BeSameAs(user);
        context.Entry(user).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task UserRepository_InsertResultAsync_ReturnsSuccessfulInsertedUser()
    {
        await using var context = CreateContext();
        var user = CreateUser("result-user");
        var repository = new UserRepository(context);

        var result = await repository.InsertResultAsync(user);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(user);
        context.Entry(user).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task UserRepository_UpdateAsync_MarksEntityAsModified()
    {
        await using var context = CreateContext();
        var user = CreateUser("update-user");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Entry(user).State = EntityState.Detached;
        var repository = new UserRepository(context);

        var updated = await repository.UpdateAsync(user);

        updated.Should().BeSameAs(user);
        context.Entry(user).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public async Task UserRepository_DeleteByPredicate_SoftDeletesMatchingUsers()
    {
        await using var context = CreateContext();
        var matching = CreateUser("matching");
        var other = CreateUser("other");
        context.Users.AddRange(matching, other);
        await context.SaveChangesAsync();
        var repository = new UserRepository(context);

        var deleted = await repository.DeleteAsync(user => user.Account == "matching");

        deleted.Should().BeTrue();
        matching.DeletedAt.Should().NotBeNull();
        other.DeletedAt.Should().BeNull();
        context.Entry(matching).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public async Task UserRepository_DeleteEntity_SoftDeletesAndTracksDetachedUser()
    {
        await using var context = CreateContext();
        var user = CreateUser("detached");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Entry(user).State = EntityState.Detached;
        var repository = new UserRepository(context);

        var deleted = await repository.DeleteAsync(user);

        deleted.Should().BeTrue();
        user.DeletedAt.Should().NotBeNull();
        context.Entry(user).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public async Task LogRepository_DeleteByPredicate_MarksMatchingLogsForDeletion()
    {
        await using var context = CreateContext();
        var matching = CreateLog("matching");
        var other = CreateLog("other");
        context.Logs.AddRange(matching, other);
        await context.SaveChangesAsync();
        var repository = new LogRepository(context);

        var deleted = await repository.DeleteAsync(log => log.EventName == "matching");

        deleted.Should().BeTrue();
        context.Entry(matching).State.Should().Be(EntityState.Deleted);
        context.Entry(other).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task LogRepository_DeleteEntity_MarksEntityForDeletion()
    {
        await using var context = CreateContext();
        var log = CreateLog("delete-entity");
        context.Logs.Add(log);
        await context.SaveChangesAsync();
        var repository = new LogRepository(context);

        var deleted = await repository.DeleteAsync(log);

        deleted.Should().BeTrue();
        context.Entry(log).State.Should().Be(EntityState.Deleted);
    }

    [Fact]
    public async Task LogRepository_FindAsync_ReturnsMatchingLog()
    {
        await using var context = CreateContext();
        var expected = CreateLog("find-me");
        context.Logs.AddRange(expected, CreateLog("other"));
        await context.SaveChangesAsync();
        var repository = new LogRepository(context);

        var actual = await repository.FindAsync(log => log.EventName == "find-me");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task LogRepository_UpdateAsync_MarksEntityAsModified()
    {
        await using var context = CreateContext();
        var log = CreateLog("update");
        context.Logs.Add(log);
        await context.SaveChangesAsync();
        context.Entry(log).State = EntityState.Detached;
        log.MethodName = "UpdatedMethod";
        var repository = new LogRepository(context);

        var updated = await repository.UpdateAsync(log);

        updated.Should().BeSameAs(log);
        context.Entry(log).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public async Task LogRepository_InsertAsync_TracksAndReturnsLog()
    {
        await using var context = CreateContext();
        var log = CreateLog("insert-log");
        var repository = new LogRepository(context);

        var inserted = await repository.InsertAsync(log);

        inserted.Should().BeSameAs(log);
        context.Entry(log).State.Should().Be(EntityState.Added);
    }

    private static ActivationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseInMemoryDatabase($"repository-tests-{Guid.NewGuid():N}")
            .Options;
        return new ActivationDbContext(
            options,
            Options.Create(new MsPlatformDbContextSettings()),
            Substitute.For<IDomainEventDispatcher>());
    }

    private static User CreateUser(string account)
        => new(
            account,
            "password",
            "salt",
            false,
            "13800000000",
            1,
            1,
            $"{account}@example.com",
            account,
            account,
            account);

    private static LogAggregateRoot CreateLog(string eventName)
        => new(
            eventName,
            "Method",
            LogEventTypeEnum.Create,
            "description",
            "content",
            1,
            "127.0.0.1",
            "13800000000");
}
