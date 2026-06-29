using System.Reflection;
using FluentAssertions;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using Xunit;

namespace MS.Microservice.Core.Tests.Domain.IdentityModel;

public sealed class UserTests
{
    [Fact]
    public void User_ShouldBeTransient_AndIgnoreDuplicateRoles()
    {
        var user = CreateUser();

        user.IsTransient().Should().BeTrue();

        user.AddRole(new Role(1, "Admin", "管理员"));
        user.AddRole(new Role(1, "Admin", "管理员"));

        user.Roles.Should().ContainSingle();
    }

    [Fact]
    public void Delete_ShouldSetDeletedAt()
    {
        var user = CreateUser();

        user.Delete();

        user.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangePassword_ShouldHashPassword()
    {
        var user = CreateUser(password: "Password123", salt: "salt-value");

        Invoke(user, "ChangePassword");

        user.Password.Should().Be(CryptologyHelper.HmacSha256("Password123salt-value"));
    }

    [Fact]
    public void Update_ShouldOnlyReplaceNonEmptyValues()
    {
        var user = CreateUser();

        Invoke(user, "Update", "New Name", null, "", "new-password", "new-salt");

        user.Name.Should().Be("New Name");
        user.Telephone.Should().Be("13800138000");
        user.Email.Should().Be("demo@example.com");
        user.Password.Should().Be("new-password");
        user.Salt.Should().Be("new-salt");
    }

    private static User CreateUser(string password = "Password123", string salt = "salt")
        => new("demo", password, salt, false, "13800138000", 1, 1, "demo@example.com", "Demo", "fz-demo", "fz-id");

    private static void Invoke(User user, string methodName, params object?[] parameters)
    {
        typeof(User)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(user, parameters);
    }
}
