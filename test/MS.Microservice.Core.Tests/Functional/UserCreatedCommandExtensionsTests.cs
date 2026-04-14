using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Users;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using System.Text;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    /// <summary>
    /// 验证创建用户示例中的纯函数式转换逻辑。
    /// 这些测试不依赖 Web 宿主，只验证命令校验、角色检查和 DTO -> 聚合映射。
    /// </summary>
    public class UserCreatedCommandExtensionsTests
    {
        [Fact]
        public void EnsureRolesExist_WhenAllRolesExist_ReturnsSome()
        {
            var command = CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            Option<UserCreatedCommand> result = command.EnsureRolesExist(roles);

            Assert.True(result.IsSome);
        }

        [Fact]
        public void EnsureRolesExist_WhenRoleMissing_ReturnsNone()
        {
            var command = CreateCommand([new RoleDto { Id = 9, Name = "Ghost" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            Option<UserCreatedCommand> result = command.EnsureRolesExist(roles);

            Assert.True(result.IsNone);
        }

        [Fact]
        public void ToDomainUser_MapsCommandIntoAggregate()
        {
            var command = CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]);
            var currentUser = new CurrentUser(7, "creator", "creator@demo.local", "13800138000", []);

            var user = command.ToDomainUser(currentUser, "salt-value");

            Assert.Equal("demo-account", user.Account);
            Assert.Equal("salt-value", user.Salt);
            Assert.Equal("示例用户", user.Name);
            Assert.Equal("13800138000", user.Telephone);
            Assert.Equal("demo@example.com", user.Email);
            Assert.Single(user.Roles);
            Assert.Equal(1, user.Roles.Single().Id);
        }

        [Fact]
        public async Task ValidateAsync_WhenCommandIsValid_ReturnsNone()
        {
            var command = CreateCommand();

            Option<string> result = await command.ValidateAsync();

            Assert.True(result.IsNone);
        }

        [Fact]
        public async Task ValidateEitherAsync_WhenCommandIsValid_ReturnsRight()
        {
            var command = CreateCommand();

            var result = await command.ValidateEitherAsync();

            Assert.True(result.IsRight);
            Assert.Same(command, result.Right);
        }

        [Fact]
        public void EnsureRolesExistEither_WhenRoleMissing_ReturnsLeft()
        {
            var command = CreateCommand([new RoleDto { Id = 9, Name = "Ghost" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            var result = command.EnsureRolesExistEither(roles);

            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
            Assert.Equal("角色校验失败", result.Left.Message);
        }

        [Fact]
        public void ToDomainUserEither_WhenPasswordIsNotBase64_ReturnsLeft()
        {
            var command = new UserCreatedCommand(
                "demo-account",
                "示例用户",
                "not-base64",
                "13800138000",
                "demo@example.com",
                []);
            var currentUser = new CurrentUser(7, "creator", "creator@demo.local", "13800138000", []);

            var result = command.ToDomainUserEither(currentUser, "salt-value");

            Assert.True(result.IsLeft);
            Assert.Equal("user.mapping", result.Left.Code);
        }

        private static UserCreatedCommand CreateCommand(List<RoleDto>? roles = null)
        {
            var rawPassword = "Password123";
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            return new UserCreatedCommand(
                account: "demo-account",
                userName: "示例用户",
                passowrd: encodedPassword,
                telephone: "13800138000",
                email: "demo@example.com",
                roles: roles ?? []);
        }
    }
}
