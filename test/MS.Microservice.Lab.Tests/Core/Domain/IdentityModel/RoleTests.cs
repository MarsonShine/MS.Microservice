using FluentAssertions;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using Xunit;
using DomainAction = MS.Microservice.Domain.Aggregates.IdentityModel.Action;

namespace MS.Microservice.Core.Tests.Domain.IdentityModel;

public sealed class RoleTests
{
    [Fact]
    public void Role_ShouldInitializeCollections_AndAddAction()
    {
        var role = new Role(1, "Admin", "管理员");

        role.Users.Should().BeEmpty();
        role.Actions.Should().BeEmpty();

        role.AddAction("ViewUsers", "/users");

        role.Actions.Should().ContainSingle();
        role.Actions[0].Should().BeOfType<DomainAction>();
        role.Actions[0].Name.Should().Be("ViewUsers");
        role.Actions[0].Path.Should().Be("/users");
    }

    [Fact]
    public void RoleComparer_ShouldCompareById()
    {
        var comparer = new RoleComparer();
        var left = new Role(1, "Admin", "管理员");
        var same = new Role(1, "Guest", "访客");
        var different = new Role(2, "Admin", "管理员");

        comparer.Equals(left, same).Should().BeTrue();
        comparer.Equals(left, different).Should().BeFalse();
        comparer.GetHashCode(left).Should().Be("Admin".GetHashCode());
    }

    [Fact]
    public void UserRole_And_RoleAction_Constructors_ShouldSetIds()
    {
        var userRole = new UserRole(3, 4);
        var roleAction = new RoleAction(5, 6);

        userRole.UserId.Should().Be(3);
        userRole.RoleId.Should().Be(4);
        roleAction.RoleId.Should().Be(5);
        roleAction.ActionId.Should().Be(6);
    }
}
