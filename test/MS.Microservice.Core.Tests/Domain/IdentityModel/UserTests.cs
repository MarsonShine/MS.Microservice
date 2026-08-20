using FluentAssertions;
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
    public void SetPasswordHash_ShouldStoreHashAndModernVersionMarker()
    {
        var user = CreateUser();

        user.SetPasswordHash("versioned-password-hash");

        user.Password.Should().Be("versioned-password-hash");
        user.Salt.Should().Be(User.ModernPasswordSaltMarker);
        user.HasModernPasswordHash().Should().BeTrue();
    }

    [Fact]
    public void Update_ShouldOnlyReplaceNonEmptyValues()
    {
        var user = CreateUser();

        typeof(User)
            .GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(user, ["New Name", null, "", "new-password", "new-salt"]);

        user.Name.Should().Be("New Name");
        user.Telephone.Should().Be("13800138000");
        user.Email.Should().Be("demo@example.com");
        user.Password.Should().Be("new-password");
        user.Salt.Should().Be("new-salt");
    }

    private static User CreateUser(string password = "Password123", string salt = "salt")
        => new("demo", password, salt, false, "13800138000", 1, 1, "demo@example.com", "Demo", "fz-demo", "fz-id");

}
